using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Iwesun.Runtime.Data;

public static class RecordStoreSchemaRegistry<TValue, TPrimaryKey>
    where TValue : IRecordStoreValue
    where TPrimaryKey : notnull
{
    internal sealed record Registration(
        string SchemaId,
        RecordStoreDefinition<TValue, TPrimaryKey> Definition,
        RecordStoreRuntimeProfile<TValue, TPrimaryKey> Profile,
        Func<RecordStore<TValue, TPrimaryKey>> RestoreFactory);

    private static readonly ConcurrentDictionary<string, Registration> Registrations =
        new(StringComparer.Ordinal);

    public static void Register(RecordStore<TValue, TPrimaryKey> store)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(store.SchemaId);
        var restorePrototype = store.CreateRegisteredRestoreOutput();
        Register(
            store.SchemaId,
            store.Definition,
            store.CaptureRuntimeProfile(),
            restorePrototype.CreateRegisteredRestoreOutput);
    }

    public static void Register(
        RecordStore<TValue, TPrimaryKey> store,
        Func<RecordStore<TValue, TPrimaryKey>> restoreFactory)
    {
        ArgumentNullException.ThrowIfNull(store);
        Register(
            store.SchemaId,
            store.Definition,
            store.CaptureRuntimeProfile(),
            restoreFactory);
    }

    public static void Register(
        string schemaId,
        RecordStoreDefinition<TValue, TPrimaryKey> definition,
        IDeepCloneStrategy<TValue>? cloneStrategy = null,
        ITableValueCodec<TValue>? codec = null,
        int indexThreshold = 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);
        ArgumentNullException.ThrowIfNull(definition);
        if (indexThreshold < 1) throw new ArgumentOutOfRangeException(nameof(indexThreshold));
        Register(
            schemaId,
            definition,
            new RecordStoreRuntimeProfile<TValue, TPrimaryKey>
            {
                CloneStrategy = cloneStrategy ?? ValueCopyCloneStrategy<TValue>.Instance,
                Codec = codec,
                IndexThreshold = indexThreshold
            },
            () => new RecordStore<TValue, TPrimaryKey>(definition));
    }

    public static void Register(
        string schemaId,
        RecordStoreDefinition<TValue, TPrimaryKey> definition,
        RecordStoreRuntimeProfile<TValue, TPrimaryKey> profile)
    {
        Register(
            schemaId,
            definition,
            profile,
            () => new RecordStore<TValue, TPrimaryKey>(definition));
    }

    public static void Register(
        string schemaId,
        RecordStoreDefinition<TValue, TPrimaryKey> definition,
        RecordStoreRuntimeProfile<TValue, TPrimaryKey> profile,
        Func<RecordStore<TValue, TPrimaryKey>> restoreFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(restoreFactory);
        if (profile.ProfileVersion < 1 || profile.IndexThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(profile));
        var registration = new Registration(
            schemaId,
            definition,
            profile.Copy(),
            restoreFactory);
        Registrations.AddOrUpdate(
            schemaId,
            registration,
            (_, current) => ReferenceEquals(current.Definition, definition)
                ? registration
                : throw new InvalidOperationException(
                    $"Schema '{schemaId}' is already registered with a different Key/PrimaryKey definition."));
    }

    internal static bool TryGet(string schemaId, out Registration? registration) =>
        Registrations.TryGetValue(schemaId, out registration);

    internal static void RefreshRuntimeProfile(RecordStore<TValue, TPrimaryKey> store)
    {
        if (!Registrations.TryGetValue(store.SchemaId, out var registration))
            return;
        if (!ReferenceEquals(registration.Definition, store.Definition))
            throw new InvalidOperationException(
                "The registered SchemaId belongs to a different Key/PrimaryKey definition.");
        Registrations[store.SchemaId] = registration with
        {
            Profile = store.CaptureRuntimeProfile().Copy()
        };
    }
}

public partial class RecordStore<TValue, TPrimaryKey>
    where TValue : IRecordStoreValue
    where TPrimaryKey : notnull
{
    private const string PersistenceFormat = "iwesun.record-store";
    private const int PersistenceFormatVersion = 3;

    public string ToJson(JsonSerializerOptions? options = null)
    {
        options ??= new JsonSerializerOptions();
        ArgumentException.ThrowIfNullOrWhiteSpace(SchemaId);
        RecordStoreSchemaRegistry<TValue, TPrimaryKey>.RefreshRuntimeProfile(this);
        var encoding = Codec is null ? "json" : "codec-base64";
        var records = new List<PersistenceRecord>(Count);
        foreach (var slot in _slots)
        {
            if (!slot.IsActive) continue;
            var value = Codec is null
                ? JsonSerializer.SerializeToElement(slot.Value, options)
                : JsonSerializer.SerializeToElement(Convert.ToBase64String(Codec.Encode(slot.Value)), options);
            records.Add(new PersistenceRecord(slot.Id.Value, value));
        }

        var document = new PersistenceDocument(
            PersistenceFormat,
            PersistenceFormatVersion,
            SchemaId,
            RuntimeProfileVersion,
            DataVersion,
            checked(_nextRecordId + 1),
            encoding,
            records.ToArray());
        return JsonSerializer.Serialize(document, options);
    }

    public static RecordStore<TValue, TPrimaryKey> RestoreJson(
        string json,
        JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        options ??= new JsonSerializerOptions();
        var document = JsonSerializer.Deserialize<PersistenceDocument>(json, options)
            ?? throw new JsonException("RecordStore persistence document is null.");
        if (!StringComparer.Ordinal.Equals(document.Format, PersistenceFormat))
            throw new JsonException($"Unsupported RecordStore format '{document.Format}'.");
        if (document.FormatVersion != PersistenceFormatVersion)
            throw new JsonException($"Unsupported RecordStore format version {document.FormatVersion}.");
        if (!RecordStoreSchemaRegistry<TValue, TPrimaryKey>.TryGet(document.SchemaId, out var registration)
            || registration is null)
            throw new JsonException($"RecordStore schema '{document.SchemaId}' is not registered.");
        if (document.ProfileVersion != registration.Profile.ProfileVersion)
            throw new JsonException(
                $"RecordStore profile version {document.ProfileVersion} does not match registered version {registration.Profile.ProfileVersion}.");
        if (document.DataVersion < 0 || document.NextRecordId < 1)
            throw new JsonException("RecordStore version or next record ID is invalid.");
        if (document.Records is null)
            throw new JsonException("RecordStore records cannot be null.");

        var restored = registration.RestoreFactory()
            ?? throw new JsonException("The registered RecordStore restore factory returned null.");
        if (!ReferenceEquals(restored.Definition, registration.Definition)
            || restored.Count != 0
            || restored.Origin is not (RecordStoreOrigin.Source or RecordStoreOrigin.Restored)
            || restored.IsReadOnly)
            throw new JsonException(
                "The registered RecordStore restore factory must return an empty mutable Source using the registered definition.");
        restored.SchemaId = registration.SchemaId;
        restored.Origin = RecordStoreOrigin.Restored;
        restored.IsReadOnly = false;
        registration.Profile.ApplyTo(restored);
        long maximumId = 0;
        foreach (var record in document.Records)
        {
            if (record.RecordId <= 0 || record.RecordId >= document.NextRecordId)
                throw new JsonException($"Record ID {record.RecordId} is outside the persisted ID range.");
            var value = DecodePersistedValue(record.Value, document.ValueEncoding, registration, options);
            if (value.StoreRecordId.Value != record.RecordId)
                throw new JsonException($"Record envelope ID {record.RecordId} does not match TValue ID {value.StoreRecordId.Value}.");
            try
            {
                restored.AppendClonedValue(value);
            }
            catch (InvalidOperationException exception)
            {
                throw new JsonException("RecordStore persistence contains an invalid or duplicate record ID.", exception);
            }
            maximumId = Math.Max(maximumId, record.RecordId);
        }
        if (document.NextRecordId <= maximumId)
            throw new JsonException("NextRecordId must be greater than every persisted record ID.");

        restored._nextRecordId = document.NextRecordId - 1;
        restored.DataVersion = document.DataVersion;
        var conflicts = restored.ValidateConstraints();
        var primaryConflict = conflicts.Any(static conflict => conflict.ConstraintName == "$primary");
        var limitConflict = conflicts.Any(static conflict => conflict.ConstraintName == "$limit");
        var uniqueConflict = conflicts.Any(static conflict =>
            conflict.ConstraintName != "$primary" && conflict.ConstraintName != "$limit");
        if ((!restored.AllowPrimaryKeyDuplicate && primaryConflict)
            || (!restored.AllowUniqueConstraintViolation && uniqueConflict)
            || limitConflict)
            throw new JsonException("RecordStore persistence violates the registered runtime profile.");
        return restored;
    }

    private static TValue DecodePersistedValue(
        JsonElement element,
        string encoding,
        RecordStoreSchemaRegistry<TValue, TPrimaryKey>.Registration registration,
        JsonSerializerOptions options)
    {
        if (StringComparer.Ordinal.Equals(encoding, "json"))
            return element.Deserialize<TValue>(options)
                ?? throw new JsonException("RecordStore JSON value cannot be null.");
        if (StringComparer.Ordinal.Equals(encoding, "codec-base64"))
        {
            if (registration.Profile.Codec is null)
                throw new JsonException($"Schema '{registration.SchemaId}' has no codec for codec-base64 data.");
            var encoded = element.GetString() ?? throw new JsonException("Codec value must be a base64 string.");
            try
            {
                return registration.Profile.Codec.Decode(Convert.FromBase64String(encoded))
                    ?? throw new JsonException("RecordStore codec returned a null value.");
            }
            catch (FormatException exception)
            {
                throw new JsonException("Codec value is not valid base64.", exception);
            }
        }
        throw new JsonException($"Unsupported RecordStore value encoding '{encoding}'.");
    }

    private sealed record PersistenceDocument(
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("formatVersion")] int FormatVersion,
        [property: JsonPropertyName("schemaId")] string SchemaId,
        [property: JsonPropertyName("profileVersion")] int ProfileVersion,
        [property: JsonPropertyName("dataVersion")] long DataVersion,
        [property: JsonPropertyName("nextRecordId")] long NextRecordId,
        [property: JsonPropertyName("valueEncoding")] string ValueEncoding,
        [property: JsonPropertyName("records")] PersistenceRecord[] Records);

    private sealed record PersistenceRecord(
        [property: JsonPropertyName("recordId")] long RecordId,
        [property: JsonPropertyName("value")] JsonElement Value);
}

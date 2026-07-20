using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Iwesun.Runtime.Data;

public static class RecordStoreV2SchemaRegistry<TValue, TPrimaryKey>
    where TValue : struct, IRecordStoreValue
    where TPrimaryKey : notnull
{
    internal sealed record Registration(
        string SchemaId,
        RecordStoreDefinition<TValue, TPrimaryKey> Definition,
        RecordStoreRuntimeProfile<TValue, TPrimaryKey> Profile);

    private static readonly ConcurrentDictionary<string, Registration> Registrations =
        new(StringComparer.Ordinal);

    public static void Register(RecordStoreV2<TValue, TPrimaryKey> store)
    {
        ArgumentNullException.ThrowIfNull(store);
        Register(store.SchemaId, store.Definition, store.CaptureRuntimeProfile());
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
        Register(schemaId, definition, new RecordStoreRuntimeProfile<TValue, TPrimaryKey>
        {
            CloneStrategy = cloneStrategy ?? ValueCopyCloneStrategy<TValue>.Instance,
            Codec = codec,
            IndexThreshold = indexThreshold
        });
    }

    public static void Register(
        string schemaId,
        RecordStoreDefinition<TValue, TPrimaryKey> definition,
        RecordStoreRuntimeProfile<TValue, TPrimaryKey> profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ProfileVersion < 1 || profile.IndexThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(profile));
        var registration = new Registration(schemaId, definition, profile.Copy());
        if (!Registrations.TryAdd(schemaId, registration))
            throw new InvalidOperationException($"Schema '{schemaId}' is already registered.");
    }

    internal static bool TryGet(string schemaId, out Registration? registration) =>
        Registrations.TryGetValue(schemaId, out registration);

    internal static void EnsureCurrentProfile(RecordStoreV2<TValue, TPrimaryKey> store)
    {
        if (!Registrations.TryGetValue(store.SchemaId, out var registration)) return;
        var current = store.CaptureRuntimeProfile();
        var registered = registration.Profile;
        var matches = ReferenceEquals(registration.Definition, store.Definition)
            && current.ProfileVersion == registered.ProfileVersion
            && ReferenceEquals(current.CloneStrategy, registered.CloneStrategy)
            && ReferenceEquals(current.Codec, registered.Codec)
            && current.IndexThreshold == registered.IndexThreshold
            && current.KeyIndexMode == registered.KeyIndexMode
            && current.AllowPrimaryKeyDuplicate == registered.AllowPrimaryKeyDuplicate
            && current.AutoMerge == registered.AutoMerge
            && current.AllowUniqueConstraintViolation == registered.AllowUniqueConstraintViolation
            && ReferenceEquals(current.ValueFormatter, registered.ValueFormatter)
            && ReferenceEquals(current.MergePredicate, registered.MergePredicate)
            && ReferenceEquals(current.MergeResolver, registered.MergeResolver)
            && ReferenceEquals(current.RecordFilter, registered.RecordFilter)
            && ReferenceEquals(current.Limit, registered.Limit)
            && Equals(current.DefaultPublishFormat, registered.DefaultPublishFormat)
            && current.UniqueConstraints.Count == registered.UniqueConstraints.Count
            && current.UniqueConstraints.Zip(registered.UniqueConstraints)
                .All(static pair => ReferenceEquals(pair.First, pair.Second));
        if (!matches)
            throw new InvalidOperationException(
                "The current Runtime Profile differs from the registered SchemaId. Use a new SchemaId and ProfileVersion.");
    }
}

public sealed partial class RecordStoreV2<TValue, TPrimaryKey>
    where TValue : struct, IRecordStoreValue
    where TPrimaryKey : notnull
{
    private const string PersistenceFormat = "iwesun.record-store";
    private const int PersistenceFormatVersion = 3;

    public string ToJson(JsonSerializerOptions? options = null)
    {
        options ??= new JsonSerializerOptions();
        ArgumentException.ThrowIfNullOrWhiteSpace(SchemaId);
        RecordStoreV2SchemaRegistry<TValue, TPrimaryKey>.EnsureCurrentProfile(this);
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

    public static RecordStoreV2<TValue, TPrimaryKey> RestoreJson(
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
        if (!RecordStoreV2SchemaRegistry<TValue, TPrimaryKey>.TryGet(document.SchemaId, out var registration)
            || registration is null)
            throw new JsonException($"RecordStore schema '{document.SchemaId}' is not registered.");
        if (document.ProfileVersion != registration.Profile.ProfileVersion)
            throw new JsonException(
                $"RecordStore profile version {document.ProfileVersion} does not match registered version {registration.Profile.ProfileVersion}.");
        if (document.DataVersion < 0 || document.NextRecordId < 1)
            throw new JsonException("RecordStore version or next record ID is invalid.");
        if (document.Records is null)
            throw new JsonException("RecordStore records cannot be null.");

        var restored = new RecordStoreV2<TValue, TPrimaryKey>(registration.Definition)
        {
            SchemaId = registration.SchemaId,
            Origin = RecordStoreV2Origin.Restored,
            IsReadOnly = false
        };
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
        RecordStoreV2SchemaRegistry<TValue, TPrimaryKey>.Registration registration,
        JsonSerializerOptions options)
    {
        if (StringComparer.Ordinal.Equals(encoding, "json"))
            return element.Deserialize<TValue>(options);
        if (StringComparer.Ordinal.Equals(encoding, "codec-base64"))
        {
            if (registration.Profile.Codec is null)
                throw new JsonException($"Schema '{registration.SchemaId}' has no codec for codec-base64 data.");
            var encoded = element.GetString() ?? throw new JsonException("Codec value must be a base64 string.");
            try
            {
                return registration.Profile.Codec.Decode(Convert.FromBase64String(encoded));
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

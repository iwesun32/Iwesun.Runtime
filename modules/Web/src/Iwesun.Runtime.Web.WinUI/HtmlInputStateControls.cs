using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Iwesun.Runtime.Web.WinUI;

internal abstract class HtmlTemporalInputControl : HtmlCursorStackPanel
{
	private bool _applyingState;

	public static readonly DependencyProperty ValueProperty =
		DependencyProperty.Register(
			nameof(Value),
			typeof(string),
			typeof(HtmlTemporalInputControl),
			new PropertyMetadata(null, OnStatePropertyChanged));

	public static readonly DependencyProperty MinimumProperty =
		DependencyProperty.Register(
			nameof(Minimum),
			typeof(string),
			typeof(HtmlTemporalInputControl),
			new PropertyMetadata(null, OnStatePropertyChanged));

	public static readonly DependencyProperty MaximumProperty =
		DependencyProperty.Register(
			nameof(Maximum),
			typeof(string),
			typeof(HtmlTemporalInputControl),
			new PropertyMetadata(null, OnStatePropertyChanged));

	public static readonly DependencyProperty StepProperty =
		DependencyProperty.Register(
			nameof(Step),
			typeof(string),
			typeof(HtmlTemporalInputControl),
			new PropertyMetadata(null, OnStatePropertyChanged));

	public static readonly DependencyProperty IsReadOnlyProperty =
		DependencyProperty.Register(
			nameof(IsReadOnly),
			typeof(bool),
			typeof(HtmlTemporalInputControl),
			new PropertyMetadata(false, OnStatePropertyChanged));

	public static readonly DependencyProperty IsEnabledProperty =
		DependencyProperty.Register(
			nameof(IsEnabled),
			typeof(bool),
			typeof(HtmlTemporalInputControl),
			new PropertyMetadata(true, OnStatePropertyChanged));

	public string? Value
	{
		get => (string?)GetValue(ValueProperty);
		set => SetValue(ValueProperty, value);
	}

	public string? Minimum
	{
		get => (string?)GetValue(MinimumProperty);
		set => SetValue(MinimumProperty, value);
	}

	public string? Maximum
	{
		get => (string?)GetValue(MaximumProperty);
		set => SetValue(MaximumProperty, value);
	}

	public string? Step
	{
		get => (string?)GetValue(StepProperty);
		set => SetValue(StepProperty, value);
	}

	public bool IsReadOnly
	{
		get => (bool)GetValue(IsReadOnlyProperty);
		set => SetValue(IsReadOnlyProperty, value);
	}

	public bool IsEnabled
	{
		get => (bool)GetValue(IsEnabledProperty);
		set => SetValue(IsEnabledProperty, value);
	}

	protected void InitializeEditors()
	{
		foreach (var calendar in Children.OfType<CalendarDatePicker>())
			calendar.DateChanged += (_, _) => UpdateValueFromEditors();
		foreach (var date in Children.OfType<DatePicker>())
			date.DateChanged += (_, _) => UpdateValueFromEditors();
		foreach (var time in Children.OfType<TimePicker>())
			time.TimeChanged += (_, _) => UpdateValueFromEditors();
		ApplyState();
	}

	private static void OnStatePropertyChanged(
		DependencyObject dependencyObject,
		DependencyPropertyChangedEventArgs args)
	{
		_ = args;
		((HtmlTemporalInputControl)dependencyObject).ApplyState();
	}

	private void ApplyState()
	{
		if (_applyingState)
			return;
		_applyingState = true;
		try
		{
			foreach (var calendar in Children.OfType<CalendarDatePicker>())
			{
				calendar.IsEnabled = IsEnabled;
				calendar.IsHitTestVisible = IsEnabled && !IsReadOnly;
				calendar.IsTabStop = IsEnabled && !IsReadOnly;
				if (TryDate(Minimum, out var minimum))
					calendar.MinDate = minimum;
				if (TryDate(Maximum, out var maximum))
					calendar.MaxDate = maximum;
			}
			foreach (var date in Children.OfType<DatePicker>())
			{
				date.IsEnabled = IsEnabled;
				date.IsHitTestVisible = IsEnabled && !IsReadOnly;
				date.IsTabStop = IsEnabled && !IsReadOnly;
				if (this is HtmlMonthInputControl)
					date.DayVisible = false;
				if (TryDate(Minimum, out var minimum))
					date.MinYear = minimum;
				if (TryDate(Maximum, out var maximum))
					date.MaxYear = maximum;
			}
			foreach (var time in Children.OfType<TimePicker>())
			{
				time.IsEnabled = IsEnabled;
				time.IsHitTestVisible = IsEnabled && !IsReadOnly;
				time.IsTabStop = IsEnabled && !IsReadOnly;
				if (double.TryParse(
					Step,
					NumberStyles.Float,
					CultureInfo.InvariantCulture,
					out var seconds)
					&& seconds >= 60
					&& seconds <= 1800
					&& seconds % 60 == 0)
				{
					time.MinuteIncrement = checked((int)(seconds / 60));
				}
			}
			ApplyValueToEditors();
		}
		finally
		{
			_applyingState = false;
		}
	}

	private void ApplyValueToEditors()
	{
		if (this is HtmlTimeInputControl)
		{
			if (TryTime(Value, out var time))
				Children.OfType<TimePicker>().Single().Time = time;
			return;
		}
		if (this is HtmlDateTimeLocalInputControl)
		{
			if (!DateTime.TryParse(
				Value,
				CultureInfo.InvariantCulture,
				DateTimeStyles.AllowWhiteSpaces,
				out var dateTime))
			{
				return;
			}
			Children.OfType<CalendarDatePicker>().Single().Date =
				new DateTimeOffset(dateTime.Date);
			Children.OfType<TimePicker>().Single().Time = dateTime.TimeOfDay;
			return;
		}
		if (!TryDate(Value, out var dateValue))
			return;
		if (this is HtmlMonthInputControl)
			Children.OfType<DatePicker>().Single().Date = dateValue;
		else
			Children.OfType<CalendarDatePicker>().Single().Date = dateValue;
	}

	private void UpdateValueFromEditors()
	{
		if (_applyingState)
			return;
		var value = this switch
		{
			HtmlDateInputControl =>
				Children.OfType<CalendarDatePicker>().Single().Date
					?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			HtmlMonthInputControl =>
				Children.OfType<DatePicker>().Single().Date
					.ToString("yyyy-MM", CultureInfo.InvariantCulture),
			HtmlWeekInputControl =>
				FormatWeek(Children.OfType<CalendarDatePicker>().Single().Date),
			HtmlTimeInputControl =>
				Children.OfType<TimePicker>().Single().Time
					.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
			HtmlDateTimeLocalInputControl =>
				FormatDateTimeLocal(
					Children.OfType<CalendarDatePicker>().Single().Date,
					Children.OfType<TimePicker>().Single().Time),
			_ => Value
		};
		if (value is not null && !string.Equals(Value, value, StringComparison.Ordinal))
			Value = value;
	}

	private bool TryDate(string? value, out DateTimeOffset result)
	{
		if (this is HtmlWeekInputControl
			&& TryWeek(value, out result))
		{
			return true;
		}
		var formats = this is HtmlMonthInputControl
			? new[] { "yyyy-MM" }
			: new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" };
		if (DateTime.TryParseExact(
			value,
			formats,
			CultureInfo.InvariantCulture,
			DateTimeStyles.None,
			out var parsed))
		{
			result = new DateTimeOffset(parsed);
			return true;
		}
		result = default;
		return false;
	}

	private static bool TryWeek(string? value, out DateTimeOffset result)
	{
		result = default;
		if (value is null
			|| value.Length != 8
			|| value[4..6] != "-W"
			|| !int.TryParse(value[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
			|| !int.TryParse(value[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var week))
		{
			return false;
		}
		try
		{
			result = new DateTimeOffset(
				ISOWeek.ToDateTime(year, week, DayOfWeek.Monday));
			return true;
		}
		catch (ArgumentOutOfRangeException)
		{
			return false;
		}
	}

	private static bool TryTime(string? value, out TimeSpan result) =>
		TimeSpan.TryParseExact(
			value,
			[@"hh\:mm", @"hh\:mm\:ss", @"hh\:mm\:ss\.FFFFFFF"],
			CultureInfo.InvariantCulture,
			out result);

	private static string? FormatWeek(DateTimeOffset? date)
	{
		if (date is null)
			return null;
		var value = date.Value.DateTime;
		return FormattableString.Invariant(
			$"{ISOWeek.GetYear(value):0000}-W{ISOWeek.GetWeekOfYear(value):00}");
	}

	private static string? FormatDateTimeLocal(
		DateTimeOffset? date,
		TimeSpan time) =>
		date is null
			? null
			: FormattableString.Invariant(
				$"{date.Value:yyyy-MM-dd}T{time:hh\\:mm}");
}

internal sealed class HtmlDateInputControl : HtmlTemporalInputControl
{
	public HtmlDateInputControl()
	{
		Children.Add(new CalendarDatePicker());
		InitializeEditors();
	}
}

internal sealed class HtmlMonthInputControl : HtmlTemporalInputControl
{
	public HtmlMonthInputControl()
	{
		Children.Add(new DatePicker());
		InitializeEditors();
	}
}

internal sealed class HtmlWeekInputControl : HtmlTemporalInputControl
{
	public HtmlWeekInputControl()
	{
		Children.Add(new CalendarDatePicker());
		InitializeEditors();
	}
}

internal sealed class HtmlTimeInputControl : HtmlTemporalInputControl
{
	public HtmlTimeInputControl()
	{
		Children.Add(new TimePicker());
		InitializeEditors();
	}
}

internal sealed class HtmlDateTimeLocalInputControl : HtmlTemporalInputControl
{
	public HtmlDateTimeLocalInputControl()
	{
		Orientation = Orientation.Horizontal;
		Children.Add(new CalendarDatePicker());
		Children.Add(new TimePicker());
		InitializeEditors();
	}
}

internal sealed class HtmlFileInputControl : HtmlCursorButton
{
	public static readonly DependencyProperty AcceptProperty =
		DependencyProperty.Register(
			nameof(Accept),
			typeof(string),
			typeof(HtmlFileInputControl),
			new PropertyMetadata(null));

	public static readonly DependencyProperty AllowsMultipleProperty =
		DependencyProperty.Register(
			nameof(AllowsMultiple),
			typeof(bool),
			typeof(HtmlFileInputControl),
			new PropertyMetadata(false));

	public string? Accept
	{
		get => (string?)GetValue(AcceptProperty);
		set => SetValue(AcceptProperty, value);
	}

	public bool AllowsMultiple
	{
		get => (bool)GetValue(AllowsMultipleProperty);
		set => SetValue(AllowsMultipleProperty, value);
	}
}

internal class HtmlFormButton : HtmlCursorButton
{
	public event EventHandler<HtmlFormCommandRequestedEventArgs>?
		CommandRequested;

	internal HtmlFormButton() => Click += OnButtonClick;

	public static readonly DependencyProperty CommandKindProperty =
		DependencyProperty.Register(
			nameof(CommandKind),
			typeof(string),
			typeof(HtmlFormButton),
			new PropertyMetadata("submit"));

	public static readonly DependencyProperty FormOwnerIdProperty =
		DependencyProperty.Register(
			nameof(FormOwnerId),
			typeof(string),
			typeof(HtmlFormButton),
			new PropertyMetadata(null));

	public static readonly DependencyProperty FormActionProperty =
		DependencyProperty.Register(
			nameof(FormAction),
			typeof(string),
			typeof(HtmlFormButton),
			new PropertyMetadata(null));

	public string? CommandKind
	{
		get => (string?)GetValue(CommandKindProperty);
		set => SetValue(CommandKindProperty, value);
	}

	public string? FormOwnerId
	{
		get => (string?)GetValue(FormOwnerIdProperty);
		set => SetValue(FormOwnerIdProperty, value);
	}

	public string? FormAction
	{
		get => (string?)GetValue(FormActionProperty);
		set => SetValue(FormActionProperty, value);
	}

	private void OnButtonClick(
		object sender,
		RoutedEventArgs args)
		=> ExecuteCommand();

	internal void ExecuteCommand()
	{
		var kind = CommandKind?.Trim().ToLowerInvariant() ?? "submit";
		var owner = ResolveFormOwner();
		if (kind == "reset" && owner is not null)
			ResetForm(owner);
		CommandRequested?.Invoke(
			this,
			new(kind, FormOwnerId, FormAction, owner));
	}

	private FrameworkElement? ResolveFormOwner()
	{
		if (!string.IsNullOrWhiteSpace(FormOwnerId))
		{
			return Find(
				XamlRoot?.Content as DependencyObject,
				element =>
					HtmlFormState.GetIsFormOwner(element)
					&& AutomationProperties.GetAutomationId(element).Equals(
						FormOwnerId,
						StringComparison.Ordinal));
		}
		DependencyObject? current = this;
		while (current is not null)
		{
			if (current is FrameworkElement element
				&& HtmlFormState.GetIsFormOwner(element))
			{
				return element;
			}
			current = ParentOf(current);
		}
		return HtmlFormState.FindContainingOwner(this);
	}

	private static void ResetForm(FrameworkElement owner)
	{
		var ownerId = AutomationProperties.GetAutomationId(owner);
		var root = owner.XamlRoot?.Content as DependencyObject ?? owner;
		foreach (var element in HtmlFormState.EnumerateLogical(root))
		{
			var explicitOwner = HtmlFormState.GetFormOwnerId(element);
			if (!IsDescendantOf(element, owner)
				&& (string.IsNullOrWhiteSpace(ownerId)
					|| !explicitOwner.Equals(
						ownerId,
						StringComparison.Ordinal)))
			{
				continue;
			}
			ResetControl(element);
		}
	}

	private static void ResetControl(FrameworkElement element)
	{
		var initial = HtmlFormState.GetInitialValue(element);
		switch (element)
		{
			case HtmlDatalistInputControl datalist:
				datalist.Text = initial;
				break;
			case TextBox text:
				text.Text = initial;
				break;
			case PasswordBox password:
				password.Password = initial;
				break;
			case HtmlCursorPasswordBoxHost passwordHost:
				passwordHost.Password = initial;
				break;
			case ToggleButton toggle:
				toggle.IsChecked =
					HtmlFormState.GetInitialChecked(toggle);
				break;
			case Slider slider when double.TryParse(
				initial,
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out var sliderValue):
				slider.Value = sliderValue;
				break;
			case NumberBox number when double.TryParse(
				initial,
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out var numberValue):
				number.Value = numberValue;
				break;
			case HtmlTemporalInputControl temporal:
				temporal.Value = initial;
				break;
			case Selector selector:
				selector.SelectedIndex = selector.Items
					.OfType<DependencyObject>()
					.Select((item, index) => new { item, index })
					.Where(static pair =>
						HtmlFormState.GetInitialSelected(pair.item))
					.Select(static pair => pair.index)
					.DefaultIfEmpty(-1)
					.First();
				break;
		}
	}

	private static bool IsDescendantOf(
		DependencyObject element,
		DependencyObject ancestor)
	{
		return HtmlFormState.EnumerateLogical(ancestor)
			.Any(item => ReferenceEquals(item, element));
	}

	private static DependencyObject? ParentOf(DependencyObject element) =>
		VisualTreeHelper.GetParent(element)
			?? (element as FrameworkElement)?.Parent;

	private static FrameworkElement? Find(
		DependencyObject? root,
		Func<FrameworkElement, bool> predicate) =>
		root is null
			? null
			: HtmlFormState.EnumerateLogical(root)
				.FirstOrDefault(predicate);
}

internal sealed record HtmlFormCommandRequestedEventArgs(
	string CommandKind,
	string? FormOwnerId,
	string? FormAction,
	FrameworkElement? FormOwner);

internal sealed class HtmlImageSubmitButton : HtmlFormButton
{
	public static readonly DependencyProperty SourceProperty =
		DependencyProperty.Register(
			nameof(Source),
			typeof(string),
			typeof(HtmlImageSubmitButton),
			new PropertyMetadata(null, OnSourceChanged));

	public string? Source
	{
		get => (string?)GetValue(SourceProperty);
		set => SetValue(SourceProperty, value);
	}

	private static void OnSourceChanged(
		DependencyObject sender,
		DependencyPropertyChangedEventArgs args)
	{
		if (sender is not HtmlImageSubmitButton button)
			return;
		var source = args.NewValue as string;
		button.Content = string.IsNullOrWhiteSpace(source)
			? null
			: new Image
			{
				Source = new BitmapImage(
					new Uri(source, UriKind.RelativeOrAbsolute))
			};
	}
}

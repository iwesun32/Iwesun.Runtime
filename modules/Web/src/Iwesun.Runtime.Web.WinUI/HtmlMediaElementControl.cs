using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed class HtmlMediaElementControl : HtmlCursorContentControl
{
	private readonly MediaPlayerElement _player = new();

	public static readonly DependencyProperty SourceProperty =
		DependencyProperty.Register(
			nameof(Source),
			typeof(string),
			typeof(HtmlMediaElementControl),
			new PropertyMetadata(null, OnMediaDefinitionChanged));

	public static readonly DependencyProperty TrackSourcesProperty =
		DependencyProperty.Register(
			nameof(TrackSources),
			typeof(string),
			typeof(HtmlMediaElementControl),
			new PropertyMetadata(string.Empty, OnMediaDefinitionChanged));

	public static readonly DependencyProperty PosterProperty =
		DependencyProperty.Register(
			nameof(Poster),
			typeof(string),
			typeof(HtmlMediaElementControl),
			new PropertyMetadata(null));

	public static readonly DependencyProperty AutoPlayProperty =
		DependencyProperty.Register(
			nameof(AutoPlay),
			typeof(bool),
			typeof(HtmlMediaElementControl),
			new PropertyMetadata(
				false,
				static (owner, args) =>
					((HtmlMediaElementControl)owner)._player.AutoPlay =
						(bool)args.NewValue));

	public static readonly DependencyProperty
		AreTransportControlsEnabledProperty =
			DependencyProperty.Register(
				nameof(AreTransportControlsEnabled),
				typeof(bool),
				typeof(HtmlMediaElementControl),
				new PropertyMetadata(
					false,
					static (owner, args) =>
						((HtmlMediaElementControl)owner)._player
							.AreTransportControlsEnabled =
								(bool)args.NewValue));

	internal HtmlMediaElementControl() => Content = _player;

	public string? Source
	{
		get => (string?)GetValue(SourceProperty);
		set => SetValue(SourceProperty, value);
	}

	public string TrackSources
	{
		get => (string)GetValue(TrackSourcesProperty);
		set => SetValue(TrackSourcesProperty, value);
	}

	public string? Poster
	{
		get => (string?)GetValue(PosterProperty);
		set => SetValue(PosterProperty, value);
	}

	public bool AutoPlay
	{
		get => (bool)GetValue(AutoPlayProperty);
		set => SetValue(AutoPlayProperty, value);
	}

	public bool AreTransportControlsEnabled
	{
		get => (bool)GetValue(AreTransportControlsEnabledProperty);
		set => SetValue(AreTransportControlsEnabledProperty, value);
	}

	private static void OnMediaDefinitionChanged(
		DependencyObject owner,
		DependencyPropertyChangedEventArgs args) =>
		((HtmlMediaElementControl)owner).ApplyMediaDefinition();

	private void ApplyMediaDefinition()
	{
		if (!Uri.TryCreate(Source, UriKind.Absolute, out var mediaUri))
		{
			_player.Source = null;
			return;
		}
		var mediaSource = MediaSource.CreateFromUri(mediaUri);
		foreach (var descriptor in TrackSources.Split(
			'\u001E',
			StringSplitOptions.RemoveEmptyEntries))
		{
			var fields = descriptor.Split('\u001F');
			if (fields.Length == 0
				|| !Uri.TryCreate(fields[0], UriKind.Absolute, out var trackUri))
			{
				continue;
			}
			mediaSource.ExternalTimedTextSources.Add(
				TimedTextSource.CreateFromUri(trackUri));
		}
		_player.Source = mediaSource;
	}
}

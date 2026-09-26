using Microsoft.Maui.Controls.Shapes;
using MorseTrainer.Domain;
using MorseTrainer.Localization;

namespace MorseTrainer.Mobile.Pages;

/// <summary>
/// «Книжка» перед курсом на телефоне: обложка раскрывается, дальше страницы — как устроен курс, метод Коха, скорость и
/// паузы, путь по шагам, как заниматься. Листается кнопками и свайпом; «Пропустить» — сразу к курсу.
/// Result: true — курс нужно начать (дочитали до «Начать шаг 1» или пропустили), false — закрыли.
/// </summary>
public sealed class CourseBookPage : ContentPage
{
    private readonly IReadOnlyList<BookPage> _pages;
    private readonly bool _startMode;
    private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Label _titleLabel;
    private readonly VerticalStackLayout _paragraphs;
    private readonly ScrollView _scroll;
    private readonly Label _counterLabel;
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private readonly Border _paper;
    private readonly Border _cover;
    private int _index;
    private bool _finished;
    private bool _start;
    private bool _coverOpened;
    private readonly string _courseTitle;

    public CourseBookPage(IReadOnlyList<BookPage> pages, bool startMode, string? bookTitle = null)
    {
        _pages = pages;
        // Название курса — в заголовке, шапке и на обложке (у второго курса своё)
        _courseTitle = bookTitle ?? Texts.T("Курс «С нуля до 60 зн/мин»");
        _startMode = startMode;
        Title = _courseTitle;
        var primaryDark = (Color)Application.Current!.Resources["PrimaryDark"];

        var root = new Grid { Padding = new Thickness(16, 12, 16, 16), RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        // Шапка: название курса и «Пропустить»
        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new Label { Text = _courseTitle, FontAttributes = FontAttributes.Bold, Opacity = 0.7, VerticalOptions = LayoutOptions.Center });
        var skip = new Button { Text = startMode ? Texts.T("Пропустить") : Texts.T("Закрыть"), BackgroundColor = Colors.Transparent, TextColor = primaryDark, Padding = new Thickness(10, 6) };
        skip.Clicked += async (_, _) => await FinishAsync(startMode);
        header.Add(skip, 1);
        root.Add(header);

        // Страница — «бумага» с корешком слева
        _titleLabel = new Label { FontSize = 24, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 0, 0, 12) };
        SetInk(_titleLabel);
        _paragraphs = new VerticalStackLayout { Spacing = 12 };
        var pageContent = new VerticalStackLayout { Children = { _titleLabel, _paragraphs } };
        _scroll = new ScrollView { Content = pageContent };
        var pageGrid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 14 };
        pageGrid.Add(new BoxView { WidthRequest = 5, CornerRadius = 3, Color = primaryDark });
        pageGrid.Add(_scroll, 1);
        _paper = new Border { Padding = new Thickness(16, 18), StrokeShape = new RoundRectangle { CornerRadius = 18 }, Content = pageGrid };
        _paper.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#FFFDF7"), Color.FromArgb("#1F1A28"));
        _paper.SetAppTheme<Brush>(Border.StrokeProperty, new SolidColorBrush(Color.FromArgb("#E6D9DE")), new SolidColorBrush(Color.FromArgb("#3A3247")));
        // Свайп и по полям страницы, и по самому тексту: касание текста забирает ScrollView, до «бумаги» оно не доходит.
        // Вертикальную прокрутку это не ломает — ScrollView перехватывает вертикальное движение у содержимого
        AddSwipes(_paper);
        AddSwipes(pageContent);
        root.Add(_paper, 0, 1);

        // Обложка поверх страницы: раскрывается от левого края, как у книги
        _cover = BuildCover();
        root.Add(_cover, 0, 1);

        // Низ: назад, номер страницы, далее
        var footer = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 10 };
        _backButton = new Button { Text = Texts.T("‹ Назад"), Padding = new Thickness(14, 8) };
        _backButton.Clicked += async (_, _) => await ShowPageAsync(_index - 1);
        footer.Add(_backButton);
        _counterLabel = new Label { HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center, Opacity = 0.65 };
        footer.Add(_counterLabel, 1);
        _nextButton = new Button { Padding = new Thickness(16, 8), BackgroundColor = (Color)Application.Current.Resources["Primary"], TextColor = Color.FromArgb("#06231C") };
        _nextButton.Clicked += async (_, _) =>
        {
            if (_index < _pages.Count - 1)
            {
                await ShowPageAsync(_index + 1);
            }
            else
            {
                await FinishAsync(_startMode);
            }
        };
        footer.Add(_nextButton, 2);
        root.Add(footer, 0, 2);

        Content = root;
        Fill(0);
    }

    public Task<bool> Result => _result.Task;

    private void AddSwipes(View view)
    {
        var swipeLeft = new SwipeGestureRecognizer { Direction = SwipeDirection.Left };
        swipeLeft.Swiped += async (_, _) => await ShowPageAsync(_index + 1);
        var swipeRight = new SwipeGestureRecognizer { Direction = SwipeDirection.Right };
        swipeRight.Swiped += async (_, _) => await ShowPageAsync(_index - 1);
        view.GestureRecognizers.Add(swipeLeft);
        view.GestureRecognizers.Add(swipeRight);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_coverOpened)
        {
            return;
        }

        _coverOpened = true;
        await Task.Delay(450);
        _cover.AnchorX = 0;
        await _cover.RotateYTo(-90, 750, Easing.CubicIn);
        _cover.IsVisible = false;
    }

    // Системная «Назад» — закрыть книжку без старта курса
    protected override bool OnBackButtonPressed()
    {
        _ = FinishAsync(false);
        return true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _result.TrySetResult(_start);
    }

    private async Task ShowPageAsync(int index)
    {
        if (index < 0 || index >= _pages.Count || index == _index)
        {
            return;
        }

        var direction = index > _index ? 1 : -1;
        Fill(index);
        // Лёгкий «перелист»: страница выезжает со стороны листания и проявляется
        _paper.Opacity = 0.2;
        _paper.TranslationX = 30 * direction;
        await Task.WhenAll(_paper.FadeTo(1, 220), _paper.TranslateTo(0, 0, 220, Easing.CubicOut));
    }

    private void Fill(int index)
    {
        _index = index;
        var page = _pages[index];
        _titleLabel.Text = page.Title;
        _paragraphs.Children.Clear();
        foreach (var paragraph in page.Paragraphs)
        {
            var label = new Label { Text = paragraph, FontSize = 16, LineHeight = 1.3, LineBreakMode = LineBreakMode.WordWrap };
            SetInk(label);
            _paragraphs.Children.Add(label);
        }

        _ = _scroll.ScrollToAsync(0, 0, false);
        _counterLabel.Text = $"{index + 1} / {_pages.Count}";
        _backButton.IsEnabled = index > 0;
        var last = index == _pages.Count - 1;
        _nextButton.Text = !last ? Texts.T("Далее ›") : _startMode ? Texts.T("Начать шаг 1") : Texts.T("Закрыть");
    }

    // Текст на «бумаге» — тёмный, как в книге (системный серый Android на светлой странице бледный)
    private static void SetInk(Label label) =>
        label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#2A2430"), Color.FromArgb("#EEE8F2"));

    private Border BuildCover()
    {
        var mark = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 0, 20) };
        mark.Add(new Ellipse { WidthRequest = 18, HeightRequest = 18, Fill = new SolidColorBrush(Colors.White), VerticalOptions = LayoutOptions.Center });
        mark.Add(new BoxView { WidthRequest = 40, HeightRequest = 15, CornerRadius = 7.5, Color = Colors.White, VerticalOptions = LayoutOptions.Center });
        mark.Add(new BoxView { WidthRequest = 40, HeightRequest = 15, CornerRadius = 7.5, Color = Colors.White, VerticalOptions = LayoutOptions.Center });
        var content = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Padding = new Thickness(24) };
        content.Add(mark);
        content.Add(new Label { Text = _courseTitle, FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center });
        content.Add(new Label { Text = "Morse Trainer", FontSize = 14, TextColor = Color.FromArgb("#CCFFFFFF"), HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
        return new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Content = content,
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb("#05B3AD"), 0),
                new GradientStop(Color.FromArgb("#02464A"), 1)
            }, new Point(0, 0), new Point(1, 1))
        };
    }

    private async Task FinishAsync(bool start)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _start = start;
        // Сначала закрываем книжку, потом отдаём результат: курс сразу переключает вкладку Shell, модальный стек
        // пустеет, и закрытие после этого падало («PopModalAsync failed because modal stack is currently empty»)
        if (Navigation.ModalStack.Contains(this))
        {
            await Navigation.PopModalAsync();
        }

        _result.TrySetResult(start);
    }
}

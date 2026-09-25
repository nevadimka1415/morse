using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using MorseTrainer.Domain;
using MorseTrainer.Localization;

namespace MorseTrainer;

/// <summary>
/// «Книжка» перед курсом: обложка раскрывается, дальше страницы — как устроен курс, метод Коха, скорость и паузы, путь по
/// шагам, как заниматься. Листается кнопками и стрелками клавиатуры; «Пропустить» — сразу к курсу.
/// startMode: книжка открыта кнопкой «Начать курс» — последняя кнопка «Начать шаг 1»; иначе просто «Закрыть».
/// Started — курс нужно начать (дочитали до конца или пропустили).
/// </summary>
public sealed class CourseBookWindow : Window
{
    private readonly IReadOnlyList<BookPage> _pages;
    private readonly bool _startMode;
    private readonly TextBlock _titleText;
    private readonly StackPanel _paragraphs;
    private readonly ScrollViewer _scroller;
    private readonly TextBlock _counterText;
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private readonly Border _cover;
    private readonly FrameworkElement _pageView;
    private int _index;

    public CourseBookWindow(IReadOnlyList<BookPage> pages, bool startMode)
    {
        ArgumentNullException.ThrowIfNull(pages);
        _pages = pages;
        _startMode = startMode;
        Title = Texts.T("Курс «С нуля до 60 зн/мин»");
        Width = 640;
        Height = 600;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Шапка: название курса и «Пропустить»
        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var courseTitle = new TextBlock { Text = Texts.T("Курс «С нуля до 60 зн/мин»"), FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        courseTitle.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        header.Children.Add(courseTitle);
        var skipButton = new Button { Content = startMode ? Texts.T("Пропустить") : Texts.T("Закрыть"), Padding = new Thickness(12, 6, 12, 6) };
        skipButton.Click += (_, _) => Finish(startMode);
        Grid.SetColumn(skipButton, 1);
        header.Children.Add(skipButton);
        root.Children.Add(header);

        // Страница: «бумага» с корешком слева
        _titleText = new TextBlock { FontFamily = new FontFamily("Georgia, Times New Roman"), FontSize = 26, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        _paragraphs = new StackPanel();
        var pageContent = new StackPanel();
        pageContent.Children.Add(_titleText);
        pageContent.Children.Add(_paragraphs);
        _scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = pageContent, Padding = new Thickness(0, 0, 8, 0) };
        var spine = new Rectangle { Width = 6, RadiusX = 3, RadiusY = 3, Margin = new Thickness(0, 0, 18, 0) };
        spine.SetResourceReference(Shape.FillProperty, "PrimaryBrush");
        var pageGrid = new Grid();
        pageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pageGrid.ColumnDefinitions.Add(new ColumnDefinition());
        pageGrid.Children.Add(spine);
        Grid.SetColumn(_scroller, 1);
        pageGrid.Children.Add(_scroller);
        var paper = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 22, 14, 22), BorderThickness = new Thickness(1), Child = pageGrid };
        paper.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        paper.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _pageView = paper;
        Grid.SetRow(paper, 1);
        root.Children.Add(paper);

        // Обложка поверх страницы: раскрывается при открытии окна
        _cover = BuildCover();
        Grid.SetRow(_cover, 1);
        root.Children.Add(_cover);

        // Низ: назад, номер страницы, далее
        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _backButton = new Button { Content = Texts.T("‹ Назад"), Padding = new Thickness(14, 8, 14, 8) };
        _backButton.Click += (_, _) => ShowPage(_index - 1);
        footer.Children.Add(_backButton);
        _counterText = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _counterText.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        Grid.SetColumn(_counterText, 1);
        footer.Children.Add(_counterText);
        _nextButton = new Button { Padding = new Thickness(16, 8, 16, 8) };
        _nextButton.SetResourceReference(StyleProperty, "PrimaryButton");
        _nextButton.Click += (_, _) =>
        {
            if (_index < _pages.Count - 1)
            {
                ShowPage(_index + 1);
            }
            else
            {
                Finish(_startMode);
            }
        };
        Grid.SetColumn(_nextButton, 2);
        footer.Children.Add(_nextButton);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        PreviewKeyDown += OnKeyDown;
        Loaded += (_, _) => OpenCover();
        ShowPage(0, animate: false);
    }

    /// <summary>Курс нужно начать: дочитали до «Начать шаг 1» или нажали «Пропустить».</summary>
    public bool Started { get; private set; }

    /// <summary>Номер открытой страницы (с 0) — для проверки в UI-тесте.</summary>
    internal int PageIndex => _index;

    internal void ShowPage(int index, bool animate = true)
    {
        if (index < 0 || index >= _pages.Count)
        {
            return;
        }

        _index = index;
        var page = _pages[index];
        _titleText.Text = page.Title;
        _paragraphs.Children.Clear();
        foreach (var paragraph in page.Paragraphs)
        {
            _paragraphs.Children.Add(new TextBlock { Text = paragraph, FontSize = 15, LineHeight = 23, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        }

        _scroller.ScrollToTop();
        _counterText.Text = $"{index + 1} / {_pages.Count}";
        _backButton.IsEnabled = index > 0;
        var last = index == _pages.Count - 1;
        _nextButton.Content = !last ? Texts.T("Далее ›") : _startMode ? Texts.T("Начать шаг 1") : Texts.T("Закрыть");
        if (animate && SystemParameters.ClientAreaAnimation)
        {
            // Лёгкий «перелист»: страница выезжает справа и проявляется
            var shift = new TranslateTransform(28, 0);
            _pageView.RenderTransform = shift;
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            _pageView.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(220)));
        }
    }

    private Border BuildCover()
    {
        var mark = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 22) };
        mark.Children.Add(new Ellipse { Width = 18, Height = 18, Fill = Brushes.White });
        mark.Children.Add(new Rectangle { Width = 40, Height = 15, RadiusX = 7.5, RadiusY = 7.5, Fill = Brushes.White, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        mark.Children.Add(new Rectangle { Width = 40, Height = 15, RadiusX = 7.5, RadiusY = 7.5, Fill = Brushes.White, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        text.Children.Add(mark);
        text.Children.Add(new TextBlock { Text = Texts.T("Курс «С нуля до 60 зн/мин»"), FontFamily = new FontFamily("Georgia, Times New Roman"), FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(30, 0, 30, 10) });
        text.Children.Add(new TextBlock { Text = "Morse Trainer", FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center });
        var cover = new Border
        {
            CornerRadius = new CornerRadius(16),
            Child = text,
            Background = new LinearGradientBrush(Color.FromRgb(0x05, 0xB3, 0xAD), Color.FromRgb(0x02, 0x46, 0x4A), 45),
            RenderTransformOrigin = new Point(0, 0.5),
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new SkewTransform(0, 0) } }
        };
        return cover;
    }

    /// <summary>Обложка «раскрывается» влево, как у настоящей книги, и открывает первую страницу.</summary>
    private async void OpenCover()
    {
        await Task.Delay(450);
        var group = (TransformGroup)_cover.RenderTransform;
        var scale = (ScaleTransform)group.Children[0];
        var skew = (SkewTransform)group.Children[1];
        var duration = TimeSpan.FromMilliseconds(750);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var open = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
        open.Completed += (_, _) => _cover.Visibility = Visibility.Collapsed;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, open);
        skew.BeginAnimation(SkewTransform.AngleYProperty, new DoubleAnimation(0, -12, duration) { EasingFunction = ease });
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Right || e.Key == Key.PageDown)
        {
            ShowPage(_index + 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Left || e.Key == Key.PageUp)
        {
            ShowPage(_index - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Finish(false);
            e.Handled = true;
        }
    }

    private void Finish(bool start)
    {
        Started = start;
        Close();
    }
}

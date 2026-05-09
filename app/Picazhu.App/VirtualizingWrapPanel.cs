using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Picazhu.App;

public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(
            nameof(ItemWidth),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(200d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(
            nameof(ItemHeight),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(200d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private Size _extent = new(0, 0);
    private Size _viewport = new(0, 0);
    private Point _offset;

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var itemsControl = ItemsControl.GetItemsOwner(this);
        var itemCount = itemsControl?.Items.Count ?? 0;
        if (itemCount == 0 || ItemWidth <= 0 || ItemHeight <= 0)
        {
            CleanupChildren();
            UpdateScrollInfo(availableSize, 0, 0);
            return availableSize;
        }

        var viewportWidth = double.IsInfinity(availableSize.Width) ? ItemWidth : availableSize.Width;
        var viewportHeight = double.IsInfinity(availableSize.Height) ? ItemHeight : availableSize.Height;
        var itemsPerRow = Math.Max(1, (int)Math.Floor(viewportWidth / ItemWidth));
        var rowCount = (int)Math.Ceiling((double)itemCount / itemsPerRow);
        var extentWidth = itemsPerRow * ItemWidth;
        var extentHeight = rowCount * ItemHeight;

        UpdateScrollInfo(new Size(viewportWidth, viewportHeight), extentWidth, extentHeight);

        var firstVisibleRow = Math.Max(0, (int)Math.Floor(VerticalOffset / ItemHeight));
        var visibleRowCount = Math.Max(1, (int)Math.Ceiling(ViewportHeight / ItemHeight) + 1);
        var startIndex = Math.Min(itemCount - 1, firstVisibleRow * itemsPerRow);
        var endIndex = Math.Min(itemCount - 1, ((firstVisibleRow + visibleRowCount) * itemsPerRow) - 1);

        if (ItemContainerGenerator is null)
        {
            return availableSize;
        }

        RealizeRange(startIndex, endIndex, itemsPerRow);

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(ItemWidth, ItemHeight));
        }

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0 || ItemWidth <= 0 || ItemHeight <= 0)
        {
            return finalSize;
        }

        var itemsPerRow = Math.Max(1, (int)Math.Floor((finalSize.Width <= 0 ? ItemWidth : finalSize.Width) / ItemWidth));
        foreach (UIElement child in InternalChildren)
        {
            if (child is not FrameworkElement element)
            {
                continue;
            }

            var itemIndex = ItemsControl.GetItemsOwner(this)?.ItemContainerGenerator.IndexFromContainer(element) ?? -1;
            if (itemIndex < 0)
            {
                continue;
            }

            var row = itemIndex / itemsPerRow;
            var column = itemIndex % itemsPerRow;
            var rect = new Rect(
                column * ItemWidth - HorizontalOffset,
                row * ItemHeight - VerticalOffset,
                ItemWidth,
                ItemHeight);

            child.Arrange(rect);
        }

        return finalSize;
    }

    public bool CanVerticallyScroll { get; set; } = true;
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetVerticalOffset(VerticalOffset - ItemHeight / 3);
    public void LineDown() => SetVerticalOffset(VerticalOffset + ItemHeight / 3);
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - SystemParameters.WheelScrollLines * ItemHeight / 3);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + SystemParameters.WheelScrollLines * ItemHeight / 3);
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void SetHorizontalOffset(double offset) { }
    public void LineLeft() { }
    public void LineRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    public void SetVerticalOffset(double offset)
    {
        var nextOffset = Math.Max(0, Math.Min(offset, Math.Max(0, ExtentHeight - ViewportHeight)));
        if (Math.Abs(nextOffset - _offset.Y) < 0.1)
        {
            return;
        }

        _offset.Y = nextOffset;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        InvalidateMeasure();
    }

    protected override void BringIndexIntoView(int index)
    {
        if (ItemWidth <= 0 || ItemHeight <= 0)
        {
            return;
        }

        var viewportWidth = _viewport.Width <= 0 ? ItemWidth : _viewport.Width;
        var itemsPerRow = Math.Max(1, (int)Math.Floor(viewportWidth / ItemWidth));
        var row = index / itemsPerRow;
        var itemTop = row * ItemHeight;
        var itemBottom = itemTop + ItemHeight;

        if (itemTop < VerticalOffset)
        {
            SetVerticalOffset(itemTop);
        }
        else if (itemBottom > VerticalOffset + ViewportHeight)
        {
            SetVerticalOffset(itemBottom - ViewportHeight);
        }
    }

    private void RealizeRange(int startIndex, int endIndex, int itemsPerRow)
    {
        RemoveItemsOutsideRange(startIndex, endIndex);

        var generator = ItemContainerGenerator;
        if (generator is null)
        {
            return;
        }

        var startPosition = generator.GeneratorPositionFromIndex(startIndex);
        var childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

        using (generator.StartAt(startPosition, GeneratorDirection.Forward, true))
        {
            for (var itemIndex = startIndex; itemIndex <= endIndex; itemIndex++, childIndex++)
            {
                var newlyRealized = generator.GenerateNext(out var isNewlyRealized) as UIElement;
                if (newlyRealized is null)
                {
                    continue;
                }

                if (isNewlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(newlyRealized);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, newlyRealized);
                    }

                    generator.PrepareItemContainer(newlyRealized);
                }

                newlyRealized.Measure(new Size(ItemWidth, ItemHeight));
            }
        }
    }

    private void RemoveItemsOutsideRange(int startIndex, int endIndex)
    {
        var generator = ItemContainerGenerator;
        if (generator is null)
        {
            return;
        }

        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var generatorPosition = new GeneratorPosition(childIndex, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(generatorPosition);
            if (itemIndex >= startIndex && itemIndex <= endIndex)
            {
                continue;
            }

            RemoveInternalChildRange(childIndex, 1);
            generator.Remove(generatorPosition, 1);
        }
    }

    private void CleanupChildren()
    {
        if (InternalChildren.Count == 0)
        {
            return;
        }

        RemoveInternalChildRange(0, InternalChildren.Count);
        ItemContainerGenerator?.RemoveAll();
    }

    private void UpdateScrollInfo(Size viewport, double extentWidth, double extentHeight)
    {
        _viewport = viewport;
        _extent = new Size(extentWidth, extentHeight);
        _offset.Y = Math.Max(0, Math.Min(_offset.Y, Math.Max(0, _extent.Height - _viewport.Height)));
        ScrollOwner?.InvalidateScrollInfo();
    }
}

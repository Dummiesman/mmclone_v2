using System;
using System.Collections.Generic;
using UnityEngine;

public class TextDropdown : UIWidget
{
    public Action<int> OnSelectedIndexChanged;

    public IReadOnlyList<string> Items => items;

    public int SelectedItemIndex
    {
        get => selectedItemIndex;
        set => SelectItem(value);
    }

    public bool LoopSelection { get; set; } = true;

    // Where the current highlight came from. Replaces hoveredItemId /
    // lastMouseHoverIndex / keyboardHighlightIndex / keyboardNavActive.
    private enum HighlightSource
    {
        None,
        Mouse,
        Keyboard
    }

    // One laid-out item. Not every item gets a placement once columns scroll,
    // so the item index has to travel with the rect.
    private struct ItemPlacement
    {
        public readonly int Index;
        public readonly Rect Rect;

        public ItemPlacement(int index, Rect rect)
        {
            Index = index;
            Rect = rect;
        }
    }

    private const float stateSize = 1f / 3f;      // drop arrow sheet is 3 vertical frames
    private const int MaxDisableableItems = 64;   // width of disabledMask

    // Mouse edge scrolling: one column immediately, then a pause, then a steady
    // repeat. Raise edgeScrollRepeatInterval to slow the repeat down.
    private const float edgeScrollInitialDelay = 0.35f;
    private const float edgeScrollRepeatInterval = 0.18f;
    private const float edgeScrollBandFraction = 0.25f;

    // 1x1 solids are identical for every dropdown, so share them. Deliberately
    // never destroyed: they outlive any single widget.
    private static Texture2D sharedBorderTexture;
    private static Texture2D sharedItemBackgroundTexture;

    private LocFont font;

    private readonly Color itemColor = new Color(1f, 1f, 0f, 1f);
    private readonly Color itemColorDisabled = new Color(0.5f, 0.5f, 0f, 1f);
    private readonly Color activeColor = Color.red;

    private Texture2D backgroundTexture;
    private Texture2D dropArrowTexture;

    private readonly List<string> items = new List<string>();
    private ulong disabledMask;
    private int selectedItemIndex = 0;

    private bool focused;
    private bool opened;

    // Rebuilt once per overlay pass. Single source of truth for drawing and
    // hit-testing, so the two can never disagree.
    private readonly List<ItemPlacement> placements = new List<ItemPlacement>();
    private Rect placementBounds;

    // Layout results the input code needs, cached from the last BuildItemLayout.
    private int layoutRowsPerColumn = 1;
    private int layoutColumnWidthPixels;
    private int maxColumnScroll;

    // Horizontal scrolling, in whole columns.
    private int columnScrollOffset;
    private bool scrollToHighlightPending;
    private int edgeScrollDirection;
    private bool edgeScrollRepeating;
    private float edgeScrollCooldown;

    private int highlightIndex = -1;
    private HighlightSource highlightSource = HighlightSource.None;
    private int mouseOverIndex = -1;
    private Vector2 lastMousePosition = new Vector2(float.MinValue, float.MinValue);

    // Layout

    private Vector2 GetItemSize()
    {
        var size = Menu.ToWidgetCoords(new Rect(0f, 0f,
            backgroundTexture.width * Menu.RenderScale,
            (backgroundTexture.height + 2) * Menu.RenderScale)); // + border

        return new Vector2(size.width, size.height);
    }

    private float GetItemsOriginY()
    {
        var background = Menu.ToWidgetCoords(new Rect(0f, 0f,
            backgroundTexture.width * Menu.RenderScale,
            (backgroundTexture.height * Menu.RenderScale) + 1f));

        return Rect.y + background.height;
    }

    /// <summary>
    /// Lays out the visible items top-to-bottom then left-to-right. If the columns
    /// overflow the screen they are first shifted left; if they still don't fit,
    /// the list scrolls horizontally and only columns from columnScrollOffset
    /// onward are placed.
    /// </summary>
    private void BuildItemLayout()
    {
        placements.Clear();
        placementBounds = new Rect(0f, 0f, 0f, 0f);
        maxColumnScroll = 0;

        if (items.Count == 0)
            return;

        Vector2 itemSize = GetItemSize();
        if (itemSize.x <= 0f || itemSize.y <= 0f)
            return;

        float originX = Rect.x;
        float originY = GetItemsOriginY();

        int rowsThatFit = Mathf.FloorToInt((1f - originY) / itemSize.y);
        layoutRowsPerColumn = Mathf.Clamp(rowsThatFit, 1, items.Count);
        int columnCount = Mathf.CeilToInt(items.Count / (float)layoutRowsPerColumn);

        // Prefer shifting the whole block left over scrolling it.
        float rightEdge = originX + (columnCount * itemSize.x);
        if (rightEdge > 1f)
            originX = Mathf.Max(0f, originX - (rightEdge - 1f));

        int fullyVisibleColumns = Mathf.Max(1, Mathf.FloorToInt((1f - originX) / itemSize.x));
        maxColumnScroll = Mathf.Max(0, columnCount - fullyVisibleColumns);

        if (scrollToHighlightPending && highlightIndex >= 0)
        {
            int highlightColumn = highlightIndex / layoutRowsPerColumn;
            if (highlightColumn < columnScrollOffset)
                columnScrollOffset = highlightColumn;
            else if (highlightColumn > columnScrollOffset + fullyVisibleColumns - 1)
                columnScrollOffset = highlightColumn - fullyVisibleColumns + 1;
        }
        scrollToHighlightPending = false;

        columnScrollOffset = Mathf.Clamp(columnScrollOffset, 0, maxColumnScroll);

        for (int column = columnScrollOffset; column < columnCount; column++)
        {
            float columnX = originX + ((column - columnScrollOffset) * itemSize.x);
            if (columnX > 1f)
                break;

            for (int row = 0; row < layoutRowsPerColumn; row++)
            {
                int index = (column * layoutRowsPerColumn) + row;
                if (index >= items.Count)
                    break;

                var widgetRect = new Rect(columnX, originY + (row * itemSize.y), itemSize.x, itemSize.y);
                Rect pixelRect = Menu.ToPixelCoordinates(widgetRect).Round();

                placements.Add(new ItemPlacement(index, pixelRect));
                layoutColumnWidthPixels = Mathf.RoundToInt(pixelRect.width);

                placementBounds = placements.Count == 1
                    ? pixelRect
                    : Rect.MinMaxRect(
                        Mathf.Min(placementBounds.xMin, pixelRect.xMin),
                        Mathf.Min(placementBounds.yMin, pixelRect.yMin),
                        Mathf.Max(placementBounds.xMax, pixelRect.xMax),
                        Mathf.Max(placementBounds.yMax, pixelRect.yMax));
            }
        }
    }

    // Scrolling

    /// <summary>
    /// -1 to scroll left, +1 to scroll right, 0 to stop
    /// </summary>
    private int GetEdgeScrollDirection(Vector2 mouse)
    {
        if (placements.Count == 0 || maxColumnScroll == 0)
            return 0;

        if (mouse.y < placementBounds.yMin || mouse.y > placementBounds.yMax)
            return 0;

        float band = layoutColumnWidthPixels * edgeScrollBandFraction;

        if (columnScrollOffset > 0 && mouse.x >= placementBounds.xMin && mouse.x <= placementBounds.xMin + band)
            return -1;

        // The rightmost column may be clipped, so take the band from whichever
        // comes first: its right edge or the screen edge.
        float rightBandEnd = Mathf.Min(placementBounds.xMax, Screen.width);
        if (columnScrollOffset < maxColumnScroll && mouse.x >= rightBandEnd - band && mouse.x <= rightBandEnd)
            return 1;

        return 0;
    }

    /// <summary>
    /// Records which way to scroll. Timing is handled in Update; this only runs
    /// during the draw pass because that is where the mouse and the column
    /// geometry are both available.
    /// </summary>
    private void SetEdgeScrollDirection(int direction)
    {
        if (direction == edgeScrollDirection)
            return;

        // Entering a band, leaving it, or reversing all restart the timing.
        edgeScrollDirection = direction;
        edgeScrollRepeating = false;
        edgeScrollCooldown = 0f; // step on the next Update
    }

    public override void Update()
    {
        base.Update();

        if (!opened || edgeScrollDirection == 0)
            return;

        edgeScrollCooldown -= Time.unscaledDeltaTime;
        if (edgeScrollCooldown > 0f)
            return;

        columnScrollOffset = Mathf.Clamp(columnScrollOffset + edgeScrollDirection, 0, maxColumnScroll);

        edgeScrollCooldown = edgeScrollRepeating ? edgeScrollRepeatInterval : edgeScrollInitialDelay;
        edgeScrollRepeating = true;
    }

    // Highlight

    private void UpdateMouseHighlight()
    {
        mouseOverIndex = -1;

        Vector2 mouse = Event.current.mousePosition;
        for (int i = 0; i < placements.Count; i++)
        {
            if (IsDisabled(placements[i].Index))
                continue;

            if (placements[i].Rect.Contains(mouse))
            {
                mouseOverIndex = placements[i].Index;
                break;
            }
        }

        bool mouseMoved = mouse != lastMousePosition;
        lastMousePosition = mouse;

        // Keyboard nav only yields once the mouse actually moves onto an item.
        if (mouseOverIndex >= 0 && (mouseMoved || highlightSource == HighlightSource.Mouse))
        {
            highlightIndex = mouseOverIndex;
            highlightSource = HighlightSource.Mouse;
        }

        SetEdgeScrollDirection(GetEdgeScrollDirection(mouse));
    }

    private void ClearHighlight()
    {
        highlightIndex = -1;
        highlightSource = HighlightSource.None;
        mouseOverIndex = -1;
        lastMousePosition = new Vector2(float.MinValue, float.MinValue);
    }

    // Drawing

    private void DrawItems()
    {
        BuildItemLayout();
        UpdateMouseHighlight();

        for (int i = 0; i < placements.Count; i++)
            DrawItem(placements[i].Index, placements[i].Rect);
    }

    private void DrawItem(int index, Rect itemRect)
    {
        GUI.DrawTexture(itemRect, sharedItemBackgroundTexture, ScaleMode.StretchToFill);

        GUI.color = IsDisabled(index) ? itemColorDisabled : itemColor;

        float inset = 1f * Menu.RenderScale;
        var textRect = new Rect(itemRect.x + inset, itemRect.y, itemRect.width - inset, itemRect.height);
        UIDrawing.ScaledLabel(textRect, items[index], font, Menu.RenderScale);

        if (index == highlightIndex)
        {
            GUI.color = Color.white;
            UIDrawing.DrawBorder(itemRect, sharedBorderTexture);
        }
    }

    private void DrawDropArrow(Rect backgroundPixelRect)
    {
        float renderScale = Menu.RenderScale;
        float arrowWidth = dropArrowTexture.width * renderScale;
        float arrowHeight = dropArrowTexture.height * stateSize * renderScale;

        var arrowRect = new Rect(
            backgroundPixelRect.x + backgroundPixelRect.width - arrowWidth,
            backgroundPixelRect.y + ((backgroundPixelRect.height - arrowHeight) * 0.5f),
            arrowWidth,
            arrowHeight);

        int frame = opened ? 2 : (focused ? 1 : 0);
        var texCoords = new Rect(0f, 1f - (frame * stateSize) - stateSize, 1f, stateSize);

        GUI.color = Color.white;
        GUI.DrawTextureWithTexCoords(arrowRect, dropArrowTexture, texCoords, true);
    }

    public override void Draw()
    {
        base.Draw();

        var oldColor = GUI.color;
        var pixelCoords = Menu.ToPixelCoordinates(Rect);

        var bgPixelCoords = new Rect(pixelCoords.x, pixelCoords.y,
            backgroundTexture.width * Menu.RenderScale,
            backgroundTexture.height * Menu.RenderScale);

        GUI.DrawTexture(bgPixelCoords, backgroundTexture, ScaleMode.StretchToFill, true);

        GUI.color = focused ? activeColor : itemColor;

        if (selectedItemIndex >= 0 && selectedItemIndex < items.Count)
        {
            float inset = 2f * Menu.RenderScale;
            var textLocation = new Rect(pixelCoords.x + inset, pixelCoords.y,
                                        pixelCoords.width - inset, pixelCoords.height);
            UIDrawing.ScaledLabel(textLocation, items[selectedItemIndex], font, Menu.RenderScale);
        }

        DrawDropArrow(bgPixelCoords);

        GUI.color = oldColor;
    }

    public override void DrawOverlay()
    {
        base.DrawOverlay();

        if (!opened)
            return;

        var oldColor = GUI.color;
        DrawItems();
        GUI.color = oldColor;
    }

    // Selection

    private void SetSelectedItemIndex(int index)
    {
        if (index == selectedItemIndex)
            return;

        selectedItemIndex = index;
        OnSelectedIndexChanged?.Invoke(index);
    }

    private void SelectItem(int index)
    {
        if (index < 0 || index >= items.Count || IsDisabled(index))
            return;

        SetSelectedItemIndex(index);
        CloseDropdown();
    }

    private void OpenDropdown()
    {
        if (opened || items.Count == 0)
            return;

        opened = true;
        ClearHighlight();
        highlightIndex = selectedItemIndex;

        // Open with the selected item's column on screen.
        columnScrollOffset = 0;
        scrollToHighlightPending = true;
        edgeScrollDirection = 0;
        edgeScrollRepeating = false;
        edgeScrollCooldown = 0f;

        Menu.FocusLock(this);
    }

    private void CloseDropdown()
    {
        if (!opened)
            return;

        opened = false;
        placements.Clear();
        columnScrollOffset = 0;
        scrollToHighlightPending = false;
        edgeScrollDirection = 0;
        edgeScrollRepeating = false;
        edgeScrollCooldown = 0f;
        ClearHighlight();
        Menu.ReleaseFocusLock(this);
    }

    private int FindNextEnabledIndex(int start, int direction)
    {
        if (items.Count == 0)
            return -1;

        int index = start;
        for (int i = 0; i < items.Count; i++)
        {
            index += direction;

            if (index >= items.Count)
            {
                if (!LoopSelection)
                    return -1;
                index = 0;
            }
            else if (index < 0)
            {
                if (!LoopSelection)
                    return -1;
                index = items.Count - 1;
            }

            if (!IsDisabled(index))
                return index;
        }

        return -1; // everything disabled
    }

    /// <summary>
    /// While open, moves the highlight and scrolls it into view; Enter commits.
    /// While closed, changes the selection directly.
    /// </summary>
    private void Navigate(int direction)
    {
        if (items.Count == 0)
            return;

        if (opened)
        {
            int from = highlightIndex >= 0 ? highlightIndex : selectedItemIndex;
            int next = FindNextEnabledIndex(from, direction);
            if (next < 0)
                return;

            highlightIndex = next;
            highlightSource = HighlightSource.Keyboard;

            // Resolved in BuildItemLayout, which is where the column geometry
            // is actually known.
            scrollToHighlightPending = true;
        }
        else
        {
            int next = FindNextEnabledIndex(selectedItemIndex, direction);
            if (next >= 0)
                SetSelectedItemIndex(next);
        }
    }

    public void Increment() => Navigate(1);

    public void Decrement() => Navigate(-1);

    // Events

    public override void Focus()
    {
        base.Focus();
        focused = true;
    }

    public override void Unfocus()
    {
        base.Unfocus();
        focused = false;
        CloseDropdown();
    }

    public override bool HandleInput(UIEvent input)
    {
        if (base.HandleInput(input))
            return true;

        if (input == UIEventType.MouseDown && focused)
        {
            if (!opened)
            {
                if (MouseContained())
                    OpenDropdown();
            }
            else if (mouseOverIndex >= 0)
            {
                SelectItem(mouseOverIndex);
            }
            else
            {
                CloseDropdown();
            }
            return true;
        }

        if (input == UIEventType.Enter && focused)
        {
            if (!opened)
                OpenDropdown();
            else
                SelectItem(highlightIndex >= 0 ? highlightIndex : selectedItemIndex);

            return true;
        }

        if (input == UIEventType.NavNext && opened)
        {
            Increment();
            return true;
        }

        if (input == UIEventType.NavPrev && opened)
        {
            Decrement();
            return true;
        }

        return false;
    }

    // Item setup

    private bool IsDisabled(int index)
    {
        if (index < 0 || index >= MaxDisableableItems)
            return false;

        return (disabledMask & (1UL << index)) != 0;
    }

    public void SetDisabledMask(ulong mask)
    {
        disabledMask = mask;

        if (!IsDisabled(selectedItemIndex))
            return;

        for (int i = 0; i < items.Count; i++)
        {
            if (!IsDisabled(i))
            {
                SetSelectedItemIndex(i);
                break;
            }
        }
    }

    public void SetItems(string pipeDelimitedItems)
    {
        SetItems(string.IsNullOrEmpty(pipeDelimitedItems)
            ? Array.Empty<string>()
            : pipeDelimitedItems.Split('|'));
    }

    public void SetItems(IEnumerable<string> newItems)
    {
        CloseDropdown();

        items.Clear();
        if (newItems != null)
            items.AddRange(newItems);

        // Indices in the mask referred to the old list, so they no longer mean
        // anything. Callers must reapply SetDisabledMask after SetItems.
        disabledMask = 0UL;

        SetSelectedItemIndex(items.Count == 0
            ? -1
            : Mathf.Clamp(selectedItemIndex, 0, items.Count - 1));
    }

    public override void Dispose()
    {
        if (opened)
        {
            opened = false;
            Menu.ReleaseFocusLock(this);
        }

        if (backgroundTexture != null) UnityEngine.Object.Destroy(backgroundTexture);
        if (dropArrowTexture != null) UnityEngine.Object.Destroy(dropArrowTexture);
    }

    // Construction

    private static Texture2D CreateSolidTexture(Color color)
    {
        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply(false);
        return texture;
    }

    private static void EnsureSharedTextures()
    {
        if (sharedBorderTexture == null)
            sharedBorderTexture = CreateSolidTexture(Color.white);

        if (sharedItemBackgroundTexture == null)
            sharedItemBackgroundTexture = CreateSolidTexture(Color.black);
    }

    public TextDropdown(UIMenu menu, string name, int id, string background, Rect rect)
        : base(menu, id, name, rect)
    {
        if (MenuManager.Instance != null)
            font = MenuManager.Instance.GetFont(MenuManager.Instance.DropdownFontSize);

        backgroundTexture = TextureLoader.Load(background);
        EnsureSharedTextures();

        Texture2D arrowBase = TextureLoader.Load("DROP_ARROW");
        Texture2D arrowMask = TextureLoader.Load("DROP_ARROW2");
        dropArrowTexture = arrowBase.SetMask(arrowMask);
        UnityEngine.Object.Destroy(arrowBase);
        UnityEngine.Object.Destroy(arrowMask);
    }
}
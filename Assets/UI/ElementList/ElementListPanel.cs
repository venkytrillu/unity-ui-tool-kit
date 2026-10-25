
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class ElementListPanel : MonoBehaviour
{

    [Header("Panel Settings")]
    [SerializeField] private float panelWidth = 450f;
    [SerializeField] private float rowHeight = 64f;
    [SerializeField] private int holdDurationMs = 600;
    [SerializeField] private float dragThreshold = 8f;

    private UIDocument document;
    private VisualElement root;
    private VisualElement panel;
    private ListView listView;
    private TextField searchField;
    private Label footerLabel;
    private Label countLabel;

    private readonly List<ElementData> allItems = new();
    private readonly List<ElementData> filteredItems = new();

    private ElementData selectedItem;

    // Pointer / drag / long-press state.
    private int activePointerId = -1;
    private Vector2 pointerStart;
    private Vector2 scrollStart;
    private ElementRow pressedRow;
    private VisualElement pressedTarget;
    private bool dragging;
    private bool longPressFired;
    private ScrollView listScrollView;
    private IVisualElementScheduledItem holdSchedule;
    private IVisualElementScheduledItem tooltipSchedule;
    private VisualElement activeTooltip;

    private class ElementData
    {
        public string id;
        public string description;
        public string type;
        public string status;
    }

    private class ElementRow : VisualElement
    {
        public ElementData Item;
        public Label Icon;
        public Label IdLabel;
        public Label DescriptionLabel;
        public Label TypeLabel;
        public Button LocateButton;
        public Button MoreButton;
        public bool SuppressNextClick;

        public ElementRow()
        {
            AddToClassList("element-row");

            Icon = new Label();
            Icon.AddToClassList("element-icon");
            Add(Icon);

            var textColumn = new VisualElement();
            textColumn.AddToClassList("element-text-column");

            IdLabel = new Label();
            IdLabel.AddToClassList("element-id");
            textColumn.Add(IdLabel);

            DescriptionLabel = new Label();
            DescriptionLabel.AddToClassList("element-description");
            textColumn.Add(DescriptionLabel);

            Add(textColumn);

            TypeLabel = new Label();
            TypeLabel.AddToClassList("element-type");
            Add(TypeLabel);

            LocateButton = new Button();
            LocateButton.text = "◎";
            LocateButton.tooltip = "Locate element";
            LocateButton.AddToClassList("icon-button");
            Add(LocateButton);

            MoreButton = new Button();
            MoreButton.text = "⋯";
            MoreButton.tooltip = "More actions";
            MoreButton.AddToClassList("icon-button");
            Add(MoreButton);
        }
    }

    private void OnEnable()
    {
        document = GetComponent<UIDocument>();
        BuildData();
        BuildUI();
    }

    private void OnDisable()
    {
        CancelPointerInteraction();
        tooltipSchedule?.Pause();
        tooltipSchedule = null;

        if (root != null)
        {
            root.UnregisterCallback<PointerDownEvent>(
                OnPointerDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<PointerMoveEvent>(
                OnPointerMove, TrickleDown.TrickleDown);
            root.UnregisterCallback<PointerUpEvent>(
                OnPointerUp, TrickleDown.TrickleDown);
            root.UnregisterCallback<PointerCancelEvent>(
                OnPointerCancel, TrickleDown.TrickleDown);

            root.Clear();
        }

        listView = null;
        listScrollView = null;
        root = null;
        panel = null;
        activeTooltip = null;
    }

    private void BuildData()
    {
        allItems.Clear();

        for (int i = 1240; i >= 1; i--)
        {
            string type = i % 3 == 0 ? "FIBER" :
                    i % 3 == 1 ? "NODE" : "COAX";

            allItems.Add(new ElementData
            {
                id = $"{type}-{i:0000}",
                description = $"Network element {i}",
                type = type,
                status = "Active"
            });
        }
    }

    private void BuildUI()
    {
        root = document.rootVisualElement;
        root.Clear();

        root.style.flexDirection = FlexDirection.Row;
        root.style.flexGrow = 1;
        root.style.backgroundColor = new Color(0.09f, 0.10f, 0.13f);

        // Shared handlers: no pointer capture.
        root.RegisterCallback<PointerDownEvent>(
            OnPointerDown, TrickleDown.TrickleDown);
        root.RegisterCallback<PointerMoveEvent>(
            OnPointerMove, TrickleDown.TrickleDown);
        root.RegisterCallback<PointerUpEvent>(
            OnPointerUp, TrickleDown.TrickleDown);
        root.RegisterCallback<PointerCancelEvent>(
            OnPointerCancel, TrickleDown.TrickleDown);

        panel = new VisualElement();
        panel.style.width = panelWidth;
        panel.style.flexGrow = 0;
        panel.style.flexShrink = 0;
        panel.style.height = Length.Percent(100);
        panel.style.minHeight = 0;
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.backgroundColor = new Color(0.09f, 0.10f, 0.13f);
        root.Add(panel);

        BuildHeader();
        BuildSearch();
        BuildList();
        BuildFooter();
    }

    private void BuildHeader()
    {
        var header = new VisualElement();
        header.style.paddingLeft = 16;
        header.style.paddingRight = 16;
        header.style.paddingTop = 14;
        header.style.paddingBottom = 12;
        header.style.backgroundColor = new Color(0.13f, 0.15f, 0.19f);
        header.style.borderBottomWidth = 1;
        header.style.borderBottomColor = new Color(0.20f, 0.22f, 0.27f);

        var title = new Label("Network Elements");
        title.style.fontSize = 17;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;
        header.Add(title);

        countLabel = new Label();
        countLabel.style.marginTop = 5;
        countLabel.style.fontSize = 11;
        countLabel.style.color = new Color(0.68f, 0.72f, 0.79f);
        header.Add(countLabel);

        panel.Add(header);
        UpdateCount();
    }

    private void BuildSearch()
    {
        searchField = new TextField();
        searchField.style.marginLeft = 12;
        searchField.style.marginRight = 12;
        searchField.style.marginTop = 12;
        searchField.style.marginBottom = 10;
        searchField.style.flexShrink = 0;
        searchField.textEdition.placeholder = "Search elements...";

        searchField.RegisterValueChangedCallback(evt =>
        {
            ApplyFilter(evt.newValue);
        });

        panel.Add(searchField);
    }

    private void BuildList()
    {
        filteredItems.Clear();
        filteredItems.AddRange(allItems);

        listView = new ListView();
        listView.name = "element-list";
        listView.itemsSource = filteredItems;
        listView.selectionType = SelectionType.None;
        listView.virtualizationMethod =
            CollectionVirtualizationMethod.FixedHeight;
        listView.fixedItemHeight = rowHeight;
        listView.reorderable = false;

        listView.style.flexGrow = 1;
        listView.style.flexShrink = 1;
        listView.style.minHeight = 0;
        listView.style.width = Length.Percent(100);
        listView.style.backgroundColor = new Color(0.09f, 0.10f, 0.13f);

        listView.makeItem = MakeItem;
        listView.bindItem = BindItem;
        listView.unbindItem = UnbindItem;

        panel.Add(listView);
        listScrollView = listView.Q<ScrollView>();
    }

    private void BuildFooter()
    {
        var footer = new VisualElement();
        footer.style.flexShrink = 0;
        footer.style.paddingLeft = 12;
        footer.style.paddingRight = 12;
        footer.style.paddingTop = 10;
        footer.style.paddingBottom = 10;
        footer.style.backgroundColor = new Color(0.13f, 0.15f, 0.19f);
        footer.style.borderTopWidth = 1;
        footer.style.borderTopColor = new Color(0.20f, 0.22f, 0.27f);

        footerLabel = new Label("No element selected");
        footerLabel.style.fontSize = 11;
        footerLabel.style.color = new Color(0.75f, 0.79f, 0.85f);
        footer.Add(footerLabel);

        panel.Add(footer);
    }

    private VisualElement MakeItem()
    {
        var row = new ElementRow();

        row.LocateButton.clicked += () =>
        {
            if (row.Item != null)
                LocateElement(row.Item);
        };

        row.MoreButton.clicked += () =>
        {
            if (row.Item != null)
                ShowMoreActions(row.Item);
        };

        // Keep action buttons independent from row selection.
        row.LocateButton.RegisterCallback<ClickEvent>(
            evt => evt.StopPropagation());

        row.MoreButton.RegisterCallback<ClickEvent>(
            evt => evt.StopPropagation());

        StyleRow(row);
        return row;
    }

    private void StyleRow(ElementRow row)
    {
        row.style.height = rowHeight;
        row.style.minHeight = rowHeight;
        row.style.maxHeight = rowHeight;
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.paddingLeft = 12;
        row.style.paddingRight = 8;
        row.style.backgroundColor = new Color(0.105f, 0.12f, 0.15f);
        row.style.borderBottomWidth = 1;
        row.style.borderBottomColor = new Color(0.18f, 0.20f, 0.24f);

        row.Icon.style.width = 30;
        row.Icon.style.minWidth = 30;
        row.Icon.style.marginRight = 8;
        row.Icon.style.fontSize = 18;
        row.Icon.style.color = new Color(0.55f, 0.72f, 1f);
        row.Icon.style.unityTextAlign = TextAnchor.MiddleCenter;

        var textColumn = row.IdLabel.parent;
        textColumn.style.flexGrow = 1;
        textColumn.style.flexShrink = 1;
        textColumn.style.minWidth = 0;

        row.IdLabel.style.fontSize = 13;
        row.IdLabel.style.color = new Color(0.94f, 0.96f, 0.99f);
        row.IdLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.IdLabel.style.overflow = Overflow.Hidden;

        row.DescriptionLabel.style.marginTop = 3;
        row.DescriptionLabel.style.fontSize = 11;
        row.DescriptionLabel.style.color = new Color(0.67f, 0.71f, 0.78f);
        row.DescriptionLabel.style.overflow = Overflow.Hidden;

        row.TypeLabel.style.width = 48;
        row.TypeLabel.style.minWidth = 48;
        row.TypeLabel.style.marginLeft = 5;
        row.TypeLabel.style.marginRight = 4;
        row.TypeLabel.style.fontSize = 9;
        row.TypeLabel.style.color = new Color(0.70f, 0.82f, 1f);
        row.TypeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;

        StyleActionButton(row.LocateButton);
        StyleActionButton(row.MoreButton);
    }

    private void StyleActionButton(Button button)
    {
        button.style.width = 30;
        button.style.minWidth = 30;
        button.style.height = 30;
        button.style.marginLeft = 2;
        button.style.marginRight = 0;
        button.style.paddingLeft = 0;
        button.style.paddingRight = 0;
        button.style.backgroundColor = Color.clear;
        button.style.color = new Color(0.78f, 0.82f, 0.88f);
        button.style.borderTopWidth = 0;
        button.style.borderBottomWidth = 0;
        button.style.borderLeftWidth = 0;
        button.style.borderRightWidth = 0;
        button.style.fontSize = 17;
    }

    private void BindItem(VisualElement element, int index)
    {
        var row = (ElementRow)element;
        var item = filteredItems[index];

        row.Item = item;
        row.SuppressNextClick = false;

        row.IdLabel.text = item.id;
        row.DescriptionLabel.text = item.description;
        row.TypeLabel.text = item.type;
        row.Icon.text = GetIcon(item.type);

        SetRowSelectedVisual(row, item == selectedItem);
    }

    private void UnbindItem(VisualElement element, int index)
    {
        var row = (ElementRow)element;

        if (pressedRow == row)
            CancelPointerInteraction();

        row.Item = null;
        row.IdLabel.text = "";
        row.DescriptionLabel.text = "";
        row.TypeLabel.text = "";
        row.Icon.text = "";
        row.SuppressNextClick = false;
        SetRowSelectedVisual(row, false);
    }

    // ---------------- Pointer, drag and long-press handling ----------------

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 || activePointerId != -1)
            return;

        var target = evt.target as VisualElement;
        var row = FindRow(target);

        if (row == null || row.Item == null)
            return;

        // Action buttons retain their native Button behaviour and do not
        // participate in row selection, long-press copying, or drag scrolling.
        if (IsActionButton(row, target))
            return;

        if (listScrollView == null && listView != null)
            listScrollView = listView.Q<ScrollView>();

        if (listScrollView == null)
            return;

        activePointerId = evt.pointerId;
        pointerStart = ToVector2(evt.position);
        scrollStart = listScrollView.scrollOffset;
        pressedRow = row;
        pressedTarget = target;
        dragging = false;
        longPressFired = false;

        // Capture only row interactions. This ensures PointerUp is received
        // if the user releases the mouse outside the panel.
        if (!root.HasPointerCapture(evt.pointerId))
            root.CapturePointer(evt.pointerId);

        // Long-press copying is allowed on the ID, description, or left icon.
        if (target == row.IdLabel ||
            target == row.DescriptionLabel ||
            target == row.Icon)
        {
            ElementRow scheduledRow = row;
            VisualElement scheduledTarget = target;

            holdSchedule = root.schedule.Execute(() =>
            {
                if (activePointerId == -1 ||
                    dragging ||
                    pressedRow != scheduledRow ||
                    scheduledRow.Item == null)
                    return;

                // Don't copy if the pointer is no longer over the list.
                if (listView == null ||
                    !listView.worldBound.Contains(pointerStart))
                    return;

                string value =
                    (scheduledTarget == scheduledRow.IdLabel ||
                     scheduledTarget == scheduledRow.Icon)
                    ? scheduledRow.Item.id
                    : scheduledRow.Item.description;

                longPressFired = true;
                CopyText(value, scheduledTarget);
            }).StartingIn(holdDurationMs);
        }

        // We handle row click selection ourselves on PointerUp because the
        // root has captured this pointer. This avoids duplicate click handlers.
        evt.StopPropagation();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (activePointerId != evt.pointerId)
            return;

        if (listView == null || listScrollView == null)
        {
            CancelPointerInteraction();
            return;
        }

        Vector2 current = ToVector2(evt.position);
        Vector2 totalDelta = current - pointerStart;

        // If the pointer leaves the list while dragging, stop immediately.
        // Releasing capture here prevents a stuck scroll state.
        if (!listView.worldBound.Contains(current))
        {
            CancelPointerInteraction();
            return;
        }

        if (!dragging &&
            totalDelta.sqrMagnitude >= dragThreshold * dragThreshold)
        {
            dragging = true;
            CancelHold();
        }

        if (!dragging)
            return;

        float contentHeight = listScrollView.contentContainer.layout.height;
        float viewportHeight = listScrollView.contentViewport.layout.height;
        float maxY = Mathf.Max(0f, contentHeight - viewportHeight);
        float newY = Mathf.Clamp(scrollStart.y - totalDelta.y, 0f, maxY);

        listScrollView.scrollOffset = new Vector2(scrollStart.x, newY);
        evt.StopPropagation();
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        if (activePointerId != evt.pointerId)
            return;

        // A normal press/release on the same row selects it once.
        // Dragging and long-press copying never select the row.
        bool pointerReleasedOverPressedRow =
            pressedRow != null &&
            pressedRow.Item != null &&
            pressedRow.worldBound.Contains(ToVector2(evt.position));

        if (!dragging &&
            !longPressFired &&
            pointerReleasedOverPressedRow)
        {
            SelectRow(pressedRow);
        }

        evt.StopPropagation();
        CancelPointerInteraction();
    }

    private void OnPointerCancel(PointerCancelEvent evt)
    {
        if (activePointerId == evt.pointerId)
            CancelPointerInteraction();
    }

    private void CancelHold()
    {
        holdSchedule?.Pause();
        holdSchedule = null;
    }

    private void CancelPointerInteraction()
    {
        CancelHold();

        int pointerToRelease = activePointerId;
        activePointerId = -1;
        pressedRow = null;
        pressedTarget = null;
        dragging = false;
        longPressFired = false;

        if (pointerToRelease != -1 &&
            root != null &&
            root.HasPointerCapture(pointerToRelease))
        {
            root.ReleasePointer(pointerToRelease);
        }
    }

    private static Vector2 ToVector2(Vector3 position)
    {
        return new Vector2(position.x, position.y);
    }

    private ElementRow FindRow(VisualElement target)
    {
        while (target != null)
        {
            if (target is ElementRow row)
                return row;

            target = target.parent;
        }

        return null;
    }

    private bool IsActionButton(ElementRow row, VisualElement target)
    {
        while (target != null && target != row)
        {
            if (target == row.LocateButton ||
                target == row.MoreButton)
                return true;

            target = target.parent;
        }

        return false;
    }

    // ---------------- Selection ----------------

    private void SelectRow(ElementRow row)
    {
        if (row == null || row.Item == null)
            return;

        selectedItem = row.Item;

        // Refresh visible virtualized rows so exactly the row matching
        // selectedItem is highlighted, even when Unity recycles row visuals.
        if (listView != null)
            listView.RefreshItems();

        if (footerLabel != null)
            footerLabel.text = $"Selected: {selectedItem.id}";

        // One log for every successful row click.
        Debug.Log($"Selected row: {selectedItem.id}");
    }

    private void SetRowSelectedVisual(ElementRow row, bool selected)
    {
        row.style.backgroundColor = selected
            ? new Color(0.16f, 0.23f, 0.32f)
            : new Color(0.105f, 0.12f, 0.15f);

        row.style.borderLeftWidth = selected ? 3 : 0;
        row.style.borderLeftColor = new Color(0.42f, 0.65f, 1f);

        row.style.paddingLeft = selected ? 9 : 12;
    }

    // ---------------- Search ----------------

    private void ApplyFilter(string query)
    {
        CancelPointerInteraction();

        filteredItems.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            filteredItems.AddRange(allItems);
        }
        else
        {
            string term = query.Trim();

            foreach (var item in allItems)
            {
                if (item.id.IndexOf(
                        term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.description.IndexOf(
                        term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.type.IndexOf(
                        term, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filteredItems.Add(item);
                }
            }
        }

        if (selectedItem != null &&
            !filteredItems.Contains(selectedItem))
        {
            selectedItem = null;
                footerLabel.text = "No element selected";
        }

        listView.RefreshItems();
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (countLabel != null)
            countLabel.text = $"{allItems.Count} elements";
    }

    // ---------------- Clipboard notification ----------------

    private void CopyText(string value, VisualElement target)
    {
        if (string.IsNullOrEmpty(value))
            return;

        GUIUtility.systemCopyBuffer = value;
        ShowCopiedTooltip(target);
    }

    private void ShowCopiedTooltip(VisualElement target)
    {
        if (root == null || target == null || target.panel == null)
            return;

        tooltipSchedule?.Pause();
        tooltipSchedule = null;

        activeTooltip?.RemoveFromHierarchy();

        var tooltip = new Label("✓  Copied!");
        activeTooltip = tooltip;

        tooltip.style.position = Position.Absolute;
        tooltip.style.backgroundColor = new Color(0.12f, 0.20f, 0.17f);
        tooltip.style.color = new Color(0.70f, 1f, 0.80f);
        tooltip.style.paddingLeft = 12;
        tooltip.style.paddingRight = 12;
        tooltip.style.paddingTop = 7;
        tooltip.style.paddingBottom = 7;
        tooltip.style.borderTopLeftRadius = 6;
        tooltip.style.borderTopRightRadius = 6;
        tooltip.style.borderBottomLeftRadius = 6;
        tooltip.style.borderBottomRightRadius = 6;
        tooltip.style.fontSize = 12;
        tooltip.style.unityFontStyleAndWeight = FontStyle.Bold;

        root.Add(tooltip);
        tooltip.BringToFront();

        Vector2 position = target.LocalToWorld(Vector2.zero);
        position = root.WorldToLocal(position);

        tooltip.style.left = Mathf.Clamp(
            position.x, 8, Mathf.Max(8, root.layout.width - 110));

        tooltip.style.top = Mathf.Clamp(
            position.y - 36, 8, Mathf.Max(8, root.layout.height - 36));

        tooltipSchedule = tooltip.schedule.Execute(() =>
        {
            if (activeTooltip == tooltip)
                activeTooltip = null;

            tooltip.RemoveFromHierarchy();
            tooltipSchedule = null;
        }).StartingIn(1200);
    }

    // ---------------- Application integration points ----------------

    private string GetIcon(string type)
    {
        return type switch
        {
            "FIBER" => "⌘",
            "NODE" => "●",
            "COAX" => "◇",
            _ => "○"
        };
    }

    private void LocateElement(ElementData item)
    {
        // Replace with your map selection / camera navigation.
        Debug.Log($"Locate requested for {item.id}");
    }

    private void ShowMoreActions(ElementData item)
    {
        // Replace with your context menu or action menu.
        Debug.Log($"More actions requested for {item.id}");
    }
}

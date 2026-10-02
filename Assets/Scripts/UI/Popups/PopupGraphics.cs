using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PopupGraphics : PopupMenuBase
{
    private MMGame game;

    // ------------------------------------------------------------------
    // Grid layout (normalized rect space) - 2 columns x 3 rows
    // ------------------------------------------------------------------
    const int kCols = 2;
    const int kRows = 3;

    const float kGridX = 0.08f;   // left edge of the grid
    const float kGridY = 0.14f;   // top edge of the grid
    const float kGridW = 0.84f;   // total grid width
    const float kCellGapX = 0.04f;   // gap between columns
    const float kCellGapY = 0.08f;   // gap between rows
    const float kRowH = 0.14f;   // height of a single cell

    // Widget metrics inside a cell
    const float kLabelH = 0.04f;
    const float kSliderH = 0.05f;
    const float kDropdownH = 0.06f;
    const float kButtonH = 0.07f;  // toggle buttons, centred on the control line
    const float kGap = 0.01f;   // header -> control
    const int kFontSize = 12;

    static float CellW => (kGridW - kCellGapX * (kCols - 1)) / kCols;

    static Rect Cell(int col, int row)
    {
        return new Rect(
            kGridX + col * (CellW + kCellGapX),
            kGridY + row * (kRowH + kCellGapY),
            CellW,
            kRowH);
    }

    // y of the control line inside a cell (below the header)
    static float ControlY(Rect cell, float controlH)
    {
        float lineY = cell.y + kLabelH + kGap;
        float lineH = Mathf.Max(kSliderH, kDropdownH);
        return lineY + (lineH - controlH) * 0.5f;
    }

    // ------------------------------------------------------------------
    // Cell builders
    // ------------------------------------------------------------------
    void AddCellLabel(string name, string header, Rect cell)
    {
        AddLabel(name + "Label", header,
                 new Rect(cell.x, cell.y, cell.width, kLabelH), kFontSize);
    }

    void AddCellSlider(string name, string header, int col, int row,
                       out UISlider slider,
                       float min = 0f, float max = 1f, bool readOnly = false)
    {
        var cell = Cell(col, row);
        AddCellLabel(name, header, cell);

        slider = AddSlider(name + "Slider",
                           new Rect(cell.x, ControlY(cell, kSliderH), cell.width, kSliderH),
                           min, max, readOnly);
    }

    void AddCellDropdown(string name, string header, int col, int row,
                         string options, out TextDropdown dropdown)
    {
        var cell = Cell(col, row);
        AddCellLabel(name, header, cell);

        dropdown = AddTextDropdown(name + "Dropdown",
                               cell.x, ControlY(cell, kDropdownH), cell.width, kDropdownH,
                               options);
    }

    // Button, no header. Sits on the same control line as the sliders beside it.
    void AddCellButton(string name, string text, int col, int row,
                       Action onClick, out TextButton button)
    {
        var cell = Cell(col, row);
        button = AddTextButton(name + "Button",
                               cell.x, ControlY(cell, kButtonH), cell.width, kButtonH,
                               text, TextNodeEffect.CenterBoth | TextNodeEffect.Box, kFontSize);

        // TODO: confirm the event/callback name on TextButton
        button.OnClick += onClick;
    }

    // ------------------------------------------------------------------
    // Widgets
    // ------------------------------------------------------------------
    TextDropdown objectDetailDropdown;      // (0,0)
    TextDropdown cloudShadowsDropdown;      // (1,0)
    UISlider visibilitySlider;              // (0,1)
    TextButton vehicleReflectionsButton;    // (1,1)
    UISlider lightingQualitySlider;         // (0,2)
    TextButton texturedSkyButton;           // (1,2)

    // ------------------------------------------------------------------
    // Handlers
    // ------------------------------------------------------------------
    private void OnObjectDetailChanged(int index)
    {
        GameState.ObjectDetail = (MMObjectDetail)index;
        game.Level.SetObjectDetail(GameState.ObjectDetail);
    }

    private void OnCloudShadowsChanged(int index)
    {
        // GameState.CloudShadows = index;
    }

    private void OnVisibilityChanged(float value)
    {
        GameState.ViewDistance = value;
        game.Level.SetViewDistance(GameState.ViewDistance);
    }

    private void OnLightingQualityChanged(float value)
    {
        // GameState.LightingQuality = value;
    }

    private void ToggleVehicleReflections()
    {
        // GameState.VehicleReflections = !GameState.VehicleReflections;
        // RefreshToggleText();
    }

    private void ToggleTexturedSky()
    {
        // GameState.TexturedSky = !GameState.TexturedSky;
        // RefreshToggleText();
    }

    private static string OnOff(bool value)
    {
        return Localization.GetString(value ? LocString.ToggleButtonOn : LocString.ToggleButtonOff);
    }

    private void RefreshToggleText()
    {
        vehicleReflectionsButton.Text =
            Localization.GetString(LocString.PopupGraphicsOptionsReflections)
            + "  " + OnOff(true);

        texturedSkyButton.Text =
            Localization.GetString(LocString.PopupGraphicsOptionsTexturedSky)
            + "  " + OnOff(true);
    }

    private void ApplyState()
    {
        game.Level.SetViewDistance(GameState.ViewDistance);
        game.Level.SetObjectDetail(GameState.ObjectDetail);
    }

    private void CancelAction()
    {
        var currentConfig = PlayerManager.CurrentPlayerConfig;
        currentConfig.SetGraphics();
        ApplyState();

        if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.PopupOptions);
    }

    private void OkAction()
    {
        var currentConfig = PlayerManager.CurrentPlayerConfig;
        var graphicsConfig = currentConfig.Gfx;

        graphicsConfig.objectDetail = GameState.ObjectDetail;
        // graphicsConfig.cloudShadows = GameState.CloudShadows;
        graphicsConfig.farClip = GameState.ViewDistance;
        // graphicsConfig.lightingQuality = GameState.LightingQuality;
        // graphicsConfig.vehicleReflections = GameState.VehicleReflections;
        // graphicsConfig.texturedSky = GameState.TexturedSky;
        currentConfig.Gfx = graphicsConfig;

        PlayerManager.SavePlayer();
        if (MenuManager.Instance != null) MenuManager.Instance.SwitchTo(MenuID.PopupOptions);
    }

    // ------------------------------------------------------------------
    public override void Activate()
    {
        base.Activate();

        objectDetailDropdown.SelectedItemIndex = (int)GameState.ObjectDetail;
        // cloudShadowsDropdown.SelectedItemIndex = GameState.CloudShadows;
        visibilitySlider.Value = GameState.ViewDistance;
        // lightingQualitySlider.Value = GameState.LightingQuality;

        RefreshToggleText();
    }

    public PopupGraphics(MMGame game) : base(MenuID.PopupGraphics, new Vector2(576, 384))
    {
        this.game = game;

        AddTitle(Localization.GetString(LocString.PopupGraphicsOptionsTitle));

        // Row 0 - dropdown, dropdown
        string objectDetailSettings = Localization.GetString(LocString.ObjQualLow);
        objectDetailSettings += "|" + Localization.GetString(LocString.ObjQualMed);
        objectDetailSettings += "|" + Localization.GetString(LocString.ObjQualHigh);
        objectDetailSettings += "|" + Localization.GetString(LocString.ObjQualHighest);

        AddCellDropdown("ObjectDetail",
                        Localization.GetString(LocString.PopupGraphicsOptionsObjectDetail),
                        0, 0, objectDetailSettings,
                        out objectDetailDropdown);

        string cloudShadowSettings = Localization.GetString(LocString.CloudShadowOff);
        cloudShadowSettings += "|" + Localization.GetString(LocString.CloudShadowLow);
        cloudShadowSettings += "|" + Localization.GetString(LocString.CloudShadowHigh);

        AddCellDropdown("CloudShadows",
                        Localization.GetString(LocString.PopupGraphicsOptionsCloudShadows),
                        1, 0, cloudShadowSettings,
                        out cloudShadowsDropdown);

        // Row 1 - slider, button
        AddCellSlider("Visibility",
                      Localization.GetString(LocString.PopupGraphicsOptionsVisibility),
                      0, 1, out visibilitySlider);

        AddCellButton("VehicleReflections",
                      Localization.GetString(LocString.PopupGraphicsOptionsReflections),
                      1, 1, ToggleVehicleReflections, out vehicleReflectionsButton);

        // Row 2 - slider, button
        AddCellSlider("LightingQuality",
                      Localization.GetString(LocString.PopupGraphicsOptionsLightingQuality),
                      0, 2, out lightingQualitySlider);

        AddCellButton("TexturedSky",
                      Localization.GetString(LocString.PopupGraphicsOptionsTexturedSky),
                      1, 2, ToggleTexturedSky, out texturedSkyButton);

        // limits
        lightingQualitySlider.Max = 2.0f;
        lightingQualitySlider.Min = 0.0f;
        lightingQualitySlider.Step = 1.0f;
        lightingQualitySlider.LockToStep = true;

        visibilitySlider.Min = 100.0f;
        visibilitySlider.Max = 1000.0f;
        visibilitySlider.Step = 10.0f;

        // events
        objectDetailDropdown.OnSelectedIndexChanged += OnObjectDetailChanged;
        cloudShadowsDropdown.OnSelectedIndexChanged += OnCloudShadowsChanged;
        visibilitySlider.OnValueChanged += OnVisibilityChanged;
        lightingQualitySlider.OnValueChanged += OnLightingQualityChanged;

        // not implemented yet:
        vehicleReflectionsButton.Enabled = false;
        cloudShadowsDropdown.Enabled = false;
        texturedSkyButton.Enabled = false;
        lightingQualitySlider.Enabled = false;

        AddOkCancel(OkAction, CancelAction);
    }
}
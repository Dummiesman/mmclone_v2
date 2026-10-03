using System.Collections.Generic;
using System.Linq;
using UnityEngine;

struct VehicleSelectStats
{
    public float Horsepower;
    public float TopSpeed;
    public float Mass;
    public float Durability;

    public void Min(VehicleSelectStats stats)
    {
        Horsepower = Mathf.Min(Horsepower, stats.Horsepower);
        TopSpeed = Mathf.Min(TopSpeed, stats.TopSpeed);
        Mass = Mathf.Min(Mass, stats.Mass);
        Durability = Mathf.Min(Durability, stats.Durability);
    }

    public void Max(VehicleSelectStats stats)
    {
        Horsepower = Mathf.Max(Horsepower, stats.Horsepower);
        TopSpeed = Mathf.Max(TopSpeed, stats.TopSpeed);
        Mass = Mathf.Max(Mass, stats.Mass);
        Durability = Mathf.Max(Durability, stats.Durability);
    }
}

public class VehicleSelect : UIMenu
{
    private GameObject vehicleFormsRoot;
    private Vehicle3DWidget vehicleWidget;

    private BMButton showcaseButton;
    private TextDropdown vehiclesDropdown;
    private TextDropdown colorsDropdown;
    private TextDropdown transmissionDropdown;

    private UIIcon carDescIcon;

    private UISlider sliderHp;
    private UISlider sliderTopSpeed;
    private UISlider sliderDurability;
    private UISlider sliderMass;

    private Dictionary<int, GameObject> vehicleForms = new Dictionary<int, GameObject>();
    private Dictionary<int, int> colorSelections = new Dictionary<int, int>();
    private Dictionary<int, AudioClip> selectionSounds = new Dictionary<int, AudioClip>();
    private Dictionary<int, float> selectionSoundEndTimes = new Dictionary<int, float>();

    private VehicleSelectStats minStats;
    private VehicleSelectStats maxStats;
    private VehicleSelectStats[] vehicleStats;

    private bool showcaseFlag = false; // used to select the proper vehicle
    private readonly BMLabel lockedSign;

    private int currentPickVehicleIndex = -1;
    private int lastUnlockedPickIndex = -1;
    private bool currentPickIsLocked = false;
    
    private bool surpressDropdownEvents = false;

    private void SetGameState()
    {
        if (lastUnlockedPickIndex >= 0)
        {
            int index = lastUnlockedPickIndex;
            var info = VehicleList.Vehicles[index];
            GameState.SelectedVehicle = info.BaseName;
            GameState.SelectedPaintjob = (colorSelections.ContainsKey(index)) ? colorSelections[index] : 0;
            GameState.TransmissionType = (transmissionDropdown.SelectedItemIndex == 0) ? MMTransmissionType.Manual : MMTransmissionType.Auto;
        }
    }

    private void LoadVehicleForm(int index)
    {
        if(!vehicleForms.TryGetValue(index, out var form))
        {
            form = new GameObject($"VehicleForm{index}");
            form.transform.parent = vehicleFormsRoot.transform;

            var info = VehicleList.Vehicles[index];
            var formComponent = form.AddComponent<VehicleForm>();
            formComponent.Load(info.BaseName);

            form.SetActive(false);
            vehicleForms[index] = form;
        }
    }

    private void ShowVehicleForm(int index)
    {
        vehicleFormsRoot.SetActive(true);
        foreach (var formKvp in vehicleForms)
        {
            formKvp.Value.SetActive(formKvp.Key == index);
        }
    }

    private void HideVehicleForm()
    {
        vehicleFormsRoot.SetActive(false);
    }

    private bool CurrentPickIsLocked()
    {
        var info = VehicleList.Vehicles[currentPickVehicleIndex];
        if (info.IsLocked)
        {
            return true;
        }
        else
        {
            int variant = 0;
            colorSelections.TryGetValue(currentPickVehicleIndex, out variant);
            if ((info.RewardFlags & (1 << variant)) != 0)
            {
                return true;
            }
        }
        return false;
    }

    private AudioClip GetSelectionSound(int index)
    {
        if (!selectionSounds.TryGetValue(index, out var clip))
        {
            string selectName = $"{VehicleList.Vehicles[index].BaseName}_select";
            clip = AudioAssetManager.LoadClip(selectName);
            selectionSounds[index] = clip; // cache nulls too, so we don't retry the load
        }
        return clip;
    }

    private void PlaySelectionSound(int index)
    {
        if (MenuManager.Instance == null) return;

        var clip = GetSelectionSound(index);
        if (clip == null) return;

        // this vehicle's clip is still playing from an earlier selection - don't stack it
        if (selectionSoundEndTimes.TryGetValue(index, out float endTime) && Time.unscaledTime < endTime)
            return;

        MenuManager.Instance.PlaySound(clip);
        selectionSoundEndTimes[index] = Time.unscaledTime + clip.length;
    }

    private void SetPick(string basename, int variant, bool alwaysPlaySelectSound = false)
    {
        int index = VehicleList.Find(basename);
        if (index < 0) return;

        var info = VehicleList.Vehicles[index];
        surpressDropdownEvents = true;

        // setup colors
        colorSelections[index] = variant;
        colorsDropdown.SetItems(string.Join("|", info.Colors));
        colorsDropdown.SelectedItemIndex = variant;

        // play selection sound if the pick index is different from last
        if (currentPickVehicleIndex != index || alwaysPlaySelectSound)
        {
            PlaySelectionSound(index);
        }

        // setup pick vars
        currentPickVehicleIndex = index;
        currentPickIsLocked = CurrentPickIsLocked();

        if(currentPickIsLocked)
        {
            lockedSign.ImageIndex = 0;
        }
        else
        {
            lockedSign.ImageIndex = -1;
            lastUnlockedPickIndex = index;
        }

        // setup showcase button
        string showcasePath = AssetManager.CombinePath("jpg", $"{info.BaseName}_show.jpg");
        showcaseButton.Enabled = AssetManager.Exists(showcasePath);

        // set distance
        vehicleWidget.TargetPolarDistance = info.UIDist;

        // setup sliders
        var stats = vehicleStats[index];
        sliderDurability.Value = stats.Durability;
        sliderMass.Value = stats.Mass;
        sliderHp.Value = stats.Horsepower;
        sliderTopSpeed.Value = stats.TopSpeed;

        // set description
        ShowVehicleDescription(index);

        // and load+show model
        LoadVehicleForm(index);
        ShowVehicleForm(index);

        // set color selection
        if (vehicleForms.TryGetValue(index, out var form))
        {
            form.GetComponent<VehicleForm>().Variant = variant;
        }

        // and set the ui state
        vehiclesDropdown.SelectedItemIndex = index;
        colorsDropdown.SelectedItemIndex = variant;
        surpressDropdownEvents = false;
    }

    private void OnSelectedColorChanged(int index)
    {
        if (surpressDropdownEvents) return;
        SetPick(VehicleList.Vehicles[currentPickVehicleIndex].BaseName, index);
    }

    private void OnSelectedVehicleChanged(int index)
    {
        if (surpressDropdownEvents) return;
        int color = 0;
        colorSelections.TryGetValue(index, out color);
        SetPick(VehicleList.Vehicles[index].BaseName, color);
    }

    private void ShowVehicleDescription(int index)
    {
        var info = VehicleList.Vehicles[index];
        bool isPro = GameState.SkillLevel == MMSkillLevel.Professional;
        colorSelections.TryGetValue(index, out int variant);

        string IfExists(string name) => AssetManager.Exists("jpg", $"{name}.jpg") ? name : null;

        string imageName = null;

        if ((info.RewardFlags & (1 << variant)) != 0)
        {
            if (isPro) imageName = IfExists($"{info.BaseName}_lck{variant}_p");
            imageName ??= IfExists($"{info.BaseName}_lck{variant}");
        }

        if (info.IsLocked)
        {
            if (isPro) imageName ??= IfExists($"{info.BaseName}_lck_p");
            imageName ??= IfExists($"{info.BaseName}_lck");
        }

        imageName ??= IfExists($"{info.BaseName}_ulck");
        carDescIcon.SetImage(imageName);
    }

    private void OpenShowcase()
    {
        if(MenuManager.Instance != null)
        {
            var vehicleInfo = VehicleList.Vehicles[vehiclesDropdown.SelectedItemIndex];
            var showcaseMenu = MenuManager.Instance.FindMenu<VehicleShowcase>();
            if(showcaseMenu != null)
            {
                showcaseFlag = true;
                showcaseMenu.SetVehicle(vehicleInfo.BaseName);
                MenuManager.Instance.SwitchTo(showcaseMenu.ID);
            }
        }
    }

    private void GoDrive()
    {
        if (currentPickIsLocked)
        {
            if (MenuManager.Instance != null) MenuManager.Instance.ShowDialog(MenuID.LockedVehicleDialog);
        }
        else
        {
            SetGameState();
            GameState.EnterGame();
        }
    }

    public override void Activate()
    {
        base.Activate();

        if (showcaseFlag)
        {
            var info = VehicleList.Vehicles[currentPickVehicleIndex];
            int variant = 0;
            colorSelections.TryGetValue(currentPickVehicleIndex, out variant); ;
            SetPick(info.BaseName, variant, true);
            showcaseFlag = false;
        }
        else
        {
            SetPick(GameState.SelectedVehicle, GameState.SelectedPaintjob, true);
        }
    }

    public override void Deactivate()
    {
        base.Deactivate();
        HideVehicleForm();
        SetGameState();
    }

    public override void Update()
    {
        base.Update();

        // rotate the active vehicle form
        if(vehicleForms.TryGetValue(vehiclesDropdown.SelectedItemIndex, out var form))
        {
            float rotationRate = -1.0f * Mathf.Rad2Deg * Time.unscaledDeltaTime;
            form.transform.Rotate(0, rotationRate, 0.0f);
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        foreach(var selectSound in  selectionSounds.Values)
        {
            UnityEngine.Object.Destroy(selectSound);
        }
    }

    private void InitStats()
    {
        float defMin = 1000000.0f;
        minStats.Horsepower = defMin;
        minStats.TopSpeed = defMin;
        minStats.Durability = defMin;
        minStats.Mass = defMin;

        maxStats.Horsepower = 0.0f;
        maxStats.TopSpeed = 0.0f;
        maxStats.Durability = 0.0f;
        maxStats.Mass = 0.0f;

        var vehList = VehicleList.Vehicles;
        vehicleStats = new VehicleSelectStats[vehList.Count];

        for (int i = 0; i < vehList.Count; i++)
        {
            var veh = vehList[i];
            var s = new VehicleSelectStats
            {
                Horsepower = veh.Horsepower,
                TopSpeed = veh.TopSpeed,
                Mass = veh.Mass,
                Durability = veh.Durability
            };
            vehicleStats[i] = s;

            minStats.Min(new VehicleSelectStats { Horsepower = s.Horsepower * 0.5f, TopSpeed = s.TopSpeed * 0.5f, Mass = s.Mass * 0.5f, Durability = s.Durability * 0.5f });
            maxStats.Max(new VehicleSelectStats { Horsepower = s.Horsepower * 1.1f, TopSpeed = s.TopSpeed * 1.1f, Mass = s.Mass * 1.1f, Durability = s.Durability * 1.1f });
        }
    }

    public VehicleSelect() : base(MenuID.VehicleSelect)
    {
        AssignBackground("veh_bk");
        AssignSwitchAudio("UIvehicles");

        InitStats();

        // VEHICLES
        string vehicleDropdownContents = string.Join("|", VehicleList.Vehicles.Select(x => x.Description));
        vehiclesDropdown = AddTextDropdown("VEHICLES", 0.0f, 0.0f, 0.1f, 0.001f, vehicleDropdownContents);
        vehiclesDropdown.OnSelectedIndexChanged += OnSelectedVehicleChanged;

        var vehDecrement = AddBMButton("roller_up", "VEHICLE UP", 1.0f, 1.0f, 3);
        var vehIncrement = AddBMButton("roller_down", "VEHICLE DOWN", 1.0f, 1.0f, 3);
        vehDecrement.OnClick += vehiclesDropdown.Decrement;
        vehIncrement.OnClick += vehiclesDropdown.Increment;

        // COLORS
        colorsDropdown = AddTextDropdown("CAR COLOR", 0.0f, 0.0f, 0.1f, 0.001f, null);
        colorsDropdown.OnSelectedIndexChanged += OnSelectedColorChanged;

        var colorDecrement = AddBMButton("roller_up", "COLOR UP", 1.0f, 1.0f, 3);
        var colorIncrement = AddBMButton("roller_down", "COLOR DOWN", 1.0f, 1.0f, 3);
        colorDecrement.OnClick += colorsDropdown.Decrement;
        colorIncrement.OnClick += colorsDropdown.Increment;

        // TRANSMISSION
        string transmissionItems = Localization.GetString(LocString.TransmissionManual);
        transmissionItems += "|";
        transmissionItems += Localization.GetString(LocString.TransmissionAutomatic);

        transmissionDropdown = AddTextDropdown("TRANSMISSION", 0.0f, 0.0f, 0.1f, 0.001f, transmissionItems, "drop_frame_bx_medium");
        transmissionDropdown.SelectedItemIndex = 1;

        var transmissionDecrement = AddBMButton("roller_up", "TRANSMISSION UP", 1.0f, 1.0f, 3);
        var transmissionIncrement = AddBMButton("roller_down", "TRANSMISSION DOWN", 1.0f, 1.0f, 3);
        transmissionDecrement.OnClick += transmissionDropdown.Decrement;
        transmissionIncrement.OnClick += transmissionDropdown.Increment;

        // SHOWCASE
        showcaseButton = AddBMButton("veh_show", 0.0f, 0.0f, 4);
        showcaseButton.OnClick += OpenShowcase;

        // GO DRIVE
        var goDriveButton = AddBMButton("veh_go", "vesel_godrive", 0.0f, 0.0f, 4);
        goDriveButton.Sound = MenuSound.GoDrive;
        goDriveButton.OnClick += GoDrive;

        // SLIDERS
        sliderHp = AddSlider("HORSEPOWER", Rect.zero, minStats.Horsepower, maxStats.Horsepower, true);
        sliderTopSpeed = AddSlider("TOP SPEED", Rect.zero, minStats.TopSpeed, maxStats.TopSpeed, true);
        sliderDurability = AddSlider("DURABILITY", Rect.zero, minStats.Durability, maxStats.Durability, true);
        sliderMass = AddSlider("MASS", Rect.zero, minStats.Mass, maxStats.Mass, true);

        // vehicle widget
        vehicleWidget = new Vehicle3DWidget(this, 1000, "Vehicle3D", new Rect(0.05f, 0.115f, 0.95f, 0.4f));
        AddWidget(vehicleWidget);

        // locked sign
        lockedSign = AddBMLabel("Locked Sign", 0.1f, 0.1f, "locked");
        lockedSign.ImageIndex = -1;

        // setup description labels
        carDescIcon = AddIcon("desc icons", 0.4844f, 0.25f);

        var descLabel = AddBMLabel("desc icons", 0.4844f, 0.25f, "veh_tsc");
        SetDescriptionLabel(descLabel);

        showcaseButton.OnFocus += () => 
        {
            carDescIcon.Visible = false;
            SetDescriptionLabelIndex(0); 
        };

        showcaseButton.OnUnfocus += () =>
        {
            carDescIcon.Visible = true;
            ClearDescriptionLabel();
        };

        vehicleFormsRoot = new GameObject($"VehicleSelect_VehicleForms");
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameState 
{
    // AUDIO
    public static MMAudioFlags AudioFlags;
    public static float AudioVolume;
    public static float MusicVolme;
    public static float AudioBalance;

    // USER SELECTION
    public static int LapCount = 1;
    public static int OpponentCount = 1;
    public static MMSkillLevel SkillLevel = MMSkillLevel.Professional;
    public static string SelectedCity = "sf";
    public static int SelectedRace = 0;
    public static string SelectedVehicle = "vpmustang99";
    public static int SelectedPaintjob = 0;
    public static MMGameMode SelectedGameMode = MMGameMode.Cruise;
    public static MMTimeOfDay SelectedTimeOfDay = MMTimeOfDay.Noon;
    public static MMWeather SelectedWeather = MMWeather.Clear;
    public static float PedestrianDensity = 0.25f;
    public static float TrafficDensity = 0.5f;
    public static float CopDensity = 1.0f;
    public static float TimeLimit = 0.0f;
    public static float Difficulty = 1.0f;
    public static MMTransmissionType TransmissionType = MMTransmissionType.Auto;

    // GFX
    public static float ViewDistance = 1000.0f;
    public static MMObjectDetail ObjectDetail = MMObjectDetail.VeryHigh;

    // INPUT
    public static MMControllerType ControllerType = MMControllerType.Keyboard;


    // HELPERS
    public static void EnterGame()
    {
        // if we have an active player, save state and save
        if (PlayerManager.CurrentPlayer != null)
        {
            var player = PlayerManager.CurrentPlayer;
            player.GameMode = GameState.SelectedGameMode;
            player.VehiclePaintjob = GameState.SelectedPaintjob;
            player.VehicleName = GameState.SelectedVehicle;
            player.City = GameState.SelectedCity;
            player.RaceId = GameState.SelectedRace;
            PlayerManager.SavePlayer();
        }

        // then move scenes
        SceneManager.LoadScene("Game");
    }

    public static void EnterMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }
}

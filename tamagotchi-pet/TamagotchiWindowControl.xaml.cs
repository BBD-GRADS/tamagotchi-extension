﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using tamagotchi_pet.Dialogs;
using tamagotchi_pet.Services;
using tamagotchi_pet.Utils;
using tamagotchi_pet.Models;
using Serilog;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using tamagotchi_pet.DTOs;
using System.Threading.Tasks;

namespace tamagotchi_pet
{
    public partial class TamagotchiWindowControl : UserControl
    {
        private DispatcherTimer gameTimer;
        private System.Timers.Timer refreshTimer;

        private Dictionary<string, string> _tokens = new Dictionary<string, string>();
        private GameService _gameService;

        public static TamagotchiWindowControl CurrentInstance { get; private set; }

        private Color inactiveColor = Colors.LightGray;
        private Color waterActiveColor = Colors.Blue;
        private Color foodActiveColor = Colors.Green;
        private Color staminaActiveColor = Colors.Yellow;

        //Time in ms
        private const double REFRESH_PERIOD = 250; //250ms

        private double simulationSpeed = 1;

        private const int TIME_FOR_POINT = 60_000; //1min
        private const int TIME_TO_FLASH = 1_000; //1s

        private const int TIME_TO_DIE = 900_000;//15min
        private const int GRACE_PERIOD_TIME = 600_000; //10min

        private const int FOOD_REFILL_TIME = 900_000; //15min
        private const int FOOD_DEPLETE_TIME = 10_800_000; //3hr

        private const int STAMINA_REFILL_TIME = 600_000; //10min
        private const int STAMINA_DEPLETE_TIME = 3_600_000; //1hr

        private const int WATER_REFILL_TIME = 300_000; //5min
        private const int WATER_DEPLETE_TIME = 3_600_000; //1hr

        private double _timeWithoutFood = 0;
        private double _timeWithoutRest = 0;
        private double _timeWithoutWater = 0;
        private double _timeTillXP = 0;
        private double _timeTillFlash = 0;

        private bool _dyingFromHunger = false;
        private bool _dyingFromExhaustion = false;
        private bool _dyingFromThirst = false;

        private bool _isEating = false;
        private bool _isResting = false;
        private bool _isDrinking = false;
        private bool _isDying = false;
        private bool isDead = false;

        private bool _isWaterAlt = false;
        private bool _isFoodAlt = false;
        private bool _isStaminaAlt = false;

        private Pet _pet = new Pet();
        private Themes _theme = Themes.Red;

        static TamagotchiWindowControl()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File("logs/tamagotchi.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }

        public TamagotchiWindowControl()
        {
            InitializeComponent();
            CurrentInstance = this;

            petImage.Visibility = Visibility.Hidden;
            restartButton.Visibility = Visibility.Visible;
            petImage.Visibility = Visibility.Hidden;

            hpBar.Value = 0;
            staminaLabel.Text = 0.ToString();
            waterLabel.Text = 0.ToString();
            foodLabel.Text = 0.ToString();
            petNameLabel.Text = string.Empty;
            xpLabel.Text = "You have no pet :(";

            Loaded += OnLoaded;
        }

        private void StartGame()
        {
            gameTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(REFRESH_PERIOD)
            };
            gameTimer.Tick += GameLoop;
            gameTimer.Start();

            Logging.Logger.Debug("StartGame: Game has started");
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Dictionary<string, string> oldTokens = TokenStorage.RetrieveTokens();
                if (oldTokens.Count == 0)
                {
                    Logging.Logger.Debug("OnLoaded: No token file found");
                    MessageBox.Show("No previous session tokens found please login.");
                }
                else
                {
                    Logging.Logger.Debug("OnLoaded: Old tokens retrieved successfully:\n" + JsonConvert.SerializeObject(oldTokens, Formatting.Indented));
                    _tokens = oldTokens;
                    await AuthFlow.RefreshTokensAsync(_tokens["id_token"], _tokens["refresh_token"]);
                    Dictionary<string, string> newTokens = TokenStorage.RetrieveTokens();
                    _tokens = newTokens;
                    Logging.Logger.Debug("OnLoaded: New tokens retrieved successfully:\n" + JsonConvert.SerializeObject(newTokens, Formatting.Indented));

                    _pet = await ApiService.GetPetAsync(_tokens["id_token"]);

                    if (_pet == null)
                    {
                        isDead = true;
                    }
                    _theme = (Themes)Enum.ToObject(typeof(Themes), await ApiService.GetThemeAsync(_tokens["id_token"]));
                }

                refreshTimer = new System.Timers.Timer(3500000); //58min
                refreshTimer.Elapsed += OnTimedRefresh;
                refreshTimer.AutoReset = true;
                refreshTimer.Enabled = true;

                _gameService = new GameService(ref petImage, ref gameCanvas, ref movementArea);
                SetTheme();
                SettingsDialog.LastSelectedTheme = _theme;

                StartGame();
            }
            catch (Exception ex)
            {
                Logging.Logger.Debug("OnLoaded: Error occured: " + ex.Message);
            }
        }

        public async Task SaveGameStateAsync()
        {
            if (!isDead)
            {
                if (_tokens.Count != 0)
                {
                    await ApiService.PutPetStatsAsync(_tokens["id_token"], new UpdatePetDTO { XP = _pet.XP, Health = _pet.Health });
                    Logging.Logger.Debug("Game state saved on document save.");
                }
            }
        }

        private void FlashTime(double delta)
        {
            _timeTillFlash += delta;
            if (_timeTillFlash >= TIME_TO_FLASH)
            {
                if (_pet.Water == 0)
                {
                    if (_isWaterAlt)
                    {
                        waterImage.Source = new BitmapImage(new Uri("pack://application:,,,/tamagotchi-pet;component/Resources/water.png"));
                    }
                    else
                    {
                        waterImage.Source = new BitmapImage(new Uri("pack://application:,,,/tamagotchi-pet;component/Resources/waterAlt.png"));
                    }
                    _isWaterAlt = !_isWaterAlt;
                }
                else
                {
                    _isWaterAlt = false;
                    waterImage.Source = new BitmapImage(new Uri("pack://application:,,,/tamagotchi-pet;component/Resources/water.png"));
                }

                if (_pet.Food == 0)
                {
                    if (_isFoodAlt)
                    {
                        foodImage.Source = new BitmapImage(new Uri("pack://application:,,,/tamagotchi-pet;component/Resources/food.png"));
                    }
                    else
                    {
                        foodImage.Source = new BitmapImage(new Uri("pack://application:,,,/tamagotchi-pet;component/Resources/foodAlt.png"));
                    }
                    _isFoodAlt = !_isFoodAlt;
                }
                else
                {
                    foodImage.Source = new BitmapImage(new Uri("pack://application:,,,/tamagotchi-pet;component/Resources/food.png"));
                    _isFoodAlt = false;
                }

                if (_pet.Stamina == 0)
                {
                    if (_isStaminaAlt)
                    {
                        staminaImage.Source = new BitmapImage(new Uri("pack://application:,,,/tamagotchi-pet;component/Resources/stamina.png"));
                    }
                    else
                    {
                        staminaImage.Source = new BitmapImage(new Uri("pack://application:,,,/tamagotchi-pet;component/Resources/staminaAlt.png"));
                    }
                    _isStaminaAlt = !_isStaminaAlt;
                }
                else
                {
                    staminaImage.Source = new BitmapImage(new Uri("pack://application:,,,/tamagotchi-pet;component/Resources/stamina.png"));
                    _isStaminaAlt = false;
                }
                _timeTillFlash = 0;
            }
        }

        private async void OnTimedRefresh(Object source, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                Logging.Logger.Debug("OnTimedRefresh: Refresh token timer elapsed.");
                var refreshed = await AuthFlow.RefreshTokensAsync(_tokens["id_token"], _tokens["refresh_token"]);
                if (refreshed)
                {
                    Dictionary<string, string> newTokens = TokenStorage.RetrieveTokens();
                    _tokens = newTokens;
                    Logging.Logger.Debug("OnTimedRefresh: New tokens :\n" + JsonConvert.SerializeObject(newTokens, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                Logging.Logger.Debug("OnTimedRefresh: Error refreshing tokens: " + ex.Message);
            }
        }

        private void SetTheme()
        {
            ImageSourceConverter imgConv = new ImageSourceConverter();
            string path;
            ImageSource imageSource;
            switch (_theme)
            {
                case Themes.Black:
                    path = "pack://application:,,,/tamagotchi-pet;component/Resources/backgroundBlack.png";
                    imageSource = (ImageSource)imgConv.ConvertFromString(path);
                    backImage.ImageSource = imageSource;

                    break;

                case Themes.Red:
                    path = "pack://application:,,,/tamagotchi-pet;component/Resources/backgroundRed.png";
                    imageSource = (ImageSource)imgConv.ConvertFromString(path);
                    backImage.ImageSource = imageSource;
                    break;

                case Themes.Green:
                    path = "pack://application:,,,/tamagotchi-pet;component/Resources/backgroundGreen.png";
                    imageSource = (ImageSource)imgConv.ConvertFromString(path);
                    backImage.ImageSource = imageSource;
                    break;

                case Themes.Blue:
                    path = "pack://application:,,,/tamagotchi-pet;component/Resources/backgroundBlue.png";
                    imageSource = (ImageSource)imgConv.ConvertFromString(path);
                    backImage.ImageSource = imageSource;
                    break;
            }
        }

        private async void GameLoop(object sender, EventArgs e)
        {
            try
            {
                double delta = REFRESH_PERIOD * simulationSpeed;
                if (_tokens.Count > 0)
                {
                    if (isDead || _pet == null)
                    {
                        restartButton.Visibility = Visibility.Visible;
                        petImage.Visibility = Visibility.Hidden;

                        hpBar.Value = 0;
                        staminaLabel.Text = 0.ToString();
                        waterLabel.Text = 0.ToString();
                        foodLabel.Text = 0.ToString();

                        petNameLabel.Text = string.Empty;
                        xpLabel.Text = "You have no pet :(";
                    }
                    else
                    {
                        _gameService.AnimatePetToPosition();
                        petNameLabel.Text = _pet.PetName;
                        xpLabel.Text = "XP: " + _pet.XP.ToString();
                        hpBar.Value = _pet.Health;

                        petImage.Visibility = Visibility.Visible;
                        restartButton.Visibility = Visibility.Hidden;

                        var petStatus = new
                        {
                            _pet.XP,
                            Health = _pet.Health.ToString("F2"),
                            Food = new
                            {
                                Level = _pet.Food.ToString("F2"),
                                IsActive = _isEating,
                                DyingFrom = _dyingFromHunger
                            },
                            Stamina = new
                            {
                                Level = _pet.Stamina.ToString("F2"),
                                IsActive = _isResting,
                                DyingFrom = _dyingFromExhaustion
                            },
                            Water = new
                            {
                                Level = _pet.Water.ToString("F2"),
                                IsActive = _isDrinking,
                                DyingFrom = _dyingFromThirst
                            },
                        };
                        Logging.Logger.Debug($"GameLoop: PET {_pet?.PetName}:\n" + JsonConvert.SerializeObject(petStatus));

                        _pet.Food = _gameService.UpdateResource(_pet.Food, FOOD_DEPLETE_TIME, FOOD_REFILL_TIME, _isEating, BtnFood, foodActiveColor, inactiveColor, delta);
                        _pet.Stamina = _gameService.UpdateResource(_pet.Stamina, STAMINA_DEPLETE_TIME, STAMINA_REFILL_TIME, _isResting, BtnStamina, staminaActiveColor, inactiveColor, delta);
                        _pet.Water = _gameService.UpdateResource(_pet.Water, WATER_DEPLETE_TIME, WATER_REFILL_TIME, _isDrinking, BtnWater, waterActiveColor, inactiveColor, delta);

                        staminaLabel.Text = _pet.Stamina.ToString("F0");
                        waterLabel.Text = _pet.Water.ToString("F0");
                        foodLabel.Text = _pet.Food.ToString("F0");

                        _gameService.UpdatePetState(_pet.Food, ref _timeWithoutFood, GRACE_PERIOD_TIME, ref _dyingFromHunger, ref _isEating, delta);
                        _gameService.UpdatePetState(_pet.Stamina, ref _timeWithoutRest, GRACE_PERIOD_TIME, ref _dyingFromExhaustion, ref _isResting, delta);
                        _gameService.UpdatePetState(_pet.Water, ref _timeWithoutWater, GRACE_PERIOD_TIME, ref _dyingFromThirst, ref _isDrinking, delta);

                        FlashTime(delta);

                        _isDying = (_dyingFromHunger || _dyingFromExhaustion || _dyingFromThirst);
                        if (_isDying)
                        {
                            _pet.Health = Math.Max(0, _pet.Health - 100 / (TIME_TO_DIE / delta));
                        }

                        if (_pet.Health == 0 && !isDead)
                        {
                            isDead = true;
                            long prevHigh = await ApiService.GetHighScoreAsync(_tokens["id_token"]);

                            await ApiService.DeletePetAsync(_tokens["id_token"]);

                            if (_pet.XP > prevHigh)
                            {
                                await ApiService.UpdateHighscoreAsync(_tokens["id_token"], _pet.XP);
                            }

                            Logging.Logger.Debug("GameLoop: Pet has died XP: " + _pet.XP);
                            restartButton.Visibility = Visibility.Visible;
                            MessageBox.Show($"Your pet died :( with a XP of {_pet.XP:F2}. Highest XP: {prevHigh:F2}");
                        }
                        else
                        {
                            _timeTillXP += delta;
                            if (_timeTillXP >= TIME_FOR_POINT)
                            {
                                _pet.XP = Math.Min(long.MaxValue, _pet.XP + 1);
                                xpLabel.Text = "XP: " + _pet.XP.ToString();
                                _timeTillXP = 0;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Logger.Debug("GameLoop: Error in game loop: " + ex.Message);
            }
        }

        private void BtnFood_Click(object sender, RoutedEventArgs e)
        {
            if (_isEating)
            {
                _isEating = false;
                BtnFood.Background = new SolidColorBrush(foodActiveColor);
            }
            else
            {
                _isEating = true;
                BtnFood.Background = new SolidColorBrush(inactiveColor);
            }
        }

        private void BtnStamina_Click(object sender, RoutedEventArgs e)
        {
            if (_isResting)
            {
                BtnStamina.Background = new SolidColorBrush(staminaActiveColor);
                _isResting = false;
            }
            else
            {
                BtnStamina.Background = new SolidColorBrush(inactiveColor);
                _isResting = true;
            }
        }

        private void BtnWater_Click(object sender, RoutedEventArgs e)
        {
            if (_isDrinking)
            {
                BtnWater.Background = new SolidColorBrush(waterActiveColor);
                _isDrinking = false;
            }
            else
            {
                BtnWater.Background = new SolidColorBrush(inactiveColor);
                _isDrinking = true;
            }
        }

        private async void BtnAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SaveGameStateAsync();
                await AuthFlow.StartAuth();
                Dictionary<string, string> retrievedTokens = TokenStorage.RetrieveTokens();
                _tokens = retrievedTokens;
                await ApiService.AuthenticateAsync(_tokens["id_token"]);
                _pet = await ApiService.GetPetAsync(_tokens["id_token"]);
                if (_pet == null)
                {
                    isDead = true;
                }

                _theme = (Themes)Enum.ToObject(typeof(Themes), await ApiService.GetThemeAsync(_tokens["id_token"]));
                SetTheme();
                SettingsDialog.LastSelectedTheme = _theme;

                Canvas.SetLeft(petImage, 106);
                Canvas.SetTop(petImage, 57);
            }
            catch (Exception ex)
            {
                Logging.Logger.Debug("BtnAccount_Click: Error logging in: " + ex.Message);
            }
        }

        //TODO SAVE STATS on close/onsave
        // await ApiService.PutPetStatsAsync(_tokens["id_Token"], _pet); TODO NB

        private async void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_tokens.Count == 0)
                {
                    Logging.Logger.Debug("OnLoaded: No token file found");
                    MessageBox.Show("No previous session tokens found please login.");
                }
                else
                {
                    _timeWithoutFood = 0;
                    _timeWithoutRest = 0;
                    _timeWithoutWater = 0;

                    _dyingFromHunger = false;
                    _dyingFromExhaustion = false;
                    _dyingFromThirst = false;

                    _isEating = false;
                    _isResting = false;
                    _isDrinking = false;
                    _isDying = false;

                    _pet = await ApiService.GetPetAsync(_tokens["id_token"]);
                    if (_pet == null || isDead)
                    {
                        CreatePetDialog inputDialog = new CreatePetDialog();
                        if (inputDialog.ShowDialog() == true)
                        {
                            _pet = await ApiService.CreatePetAsync(_tokens["id_token"], inputDialog.ResponseText);
                            if (_pet != null)
                            {
                                isDead = false;
                            }
                            else
                            {
                                isDead = true;
                            }
                            Logging.Logger.Debug("BtnRestart_Click: Pet created: " + _pet?.PetName);
                            Canvas.SetLeft(petImage, 106);
                            Canvas.SetTop(petImage, 57);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Logger.Debug("BtnRestart_Click: Error restarting game: " + ex.Message);
            }
        }

        private async void BtnSettings_Clicked(object sender, RoutedEventArgs e)
        {
            try
            {
                SettingsDialog settingsDialog = new SettingsDialog();

                if (settingsDialog.ShowDialog() == true)
                {
                    _theme = settingsDialog.SelectedTheme;
                    simulationSpeed = settingsDialog.SimulationSpeed;
                    SetTheme();
                    await ApiService.PutThemeAsync(_tokens["id_token"], (int)_theme);
                }
            }
            catch (Exception ex)
            {
                Logging.Logger.Debug("BtnSettings_Clicked: Error saving settings: " + ex.Message);
            }
        }
    }
}
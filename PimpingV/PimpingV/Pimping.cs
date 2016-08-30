namespace PimpingV
{
    // System
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.Drawing;

    // RPH
    using Rage;
    using Rage.Native;

    internal static class Pimping
    {
        public static readonly Random Random = new Random();

        public static readonly StaticFinalizer Finalizer = new StaticFinalizer(FinalizerCleanUp);

        public static readonly Settings UserSettings = new Settings(@"Plugins\PimpingV Configuration.ini");

        /// <summary>
        /// The possible prostitutes models.
        /// </summary>
        public static readonly Model[] ProstitutesPedsModels = { "s_f_y_hooker_01", "s_f_y_hooker_02", "s_f_y_hooker_03" };

        public static readonly Model[] CustomersMalePedsModels = Model.PedModels.Where(m => m.Name[2] == 'M').ToArray(); // get male ped models

        public const int MoneyRewardMultiplier = 300;

        public const double MissionTimeLimitInSeconds = 90;

        public const float SpawnMaxRadius = 265.0f;

        public static readonly RelationshipGroup GirlsRelationshipGroup = "PIMPING_GIRL";

        // video showing the pimp missions from GTA San Andreas: https://www.youtube.com/watch?v=UlFX2DVzSrY
        #region Dialogues
        public static readonly string[] FirstProstitutePickSubtitleText =
        {
            "There's a ~b~girl~s~ nearby - go pick her up.",
        };

        public static readonly string[] SecondProstitutePickSubtitleText =
        {
            "Good! Go and pick up another ~b~girl~s~.",
        };

        public static readonly string[] ProstitutePickSubtitleText =
        {
            "Your ~b~girl~s~ has finished with a customer. Go and pick her up.",
        };

        public static readonly string[] DestinationSubtitleText =
        {
            "~y~PLACE_NAME~s~ next.",
            "Drive to the ~y~PLACE_NAME~s~ to find a customer for your girl.",
            "~y~PLACE_NAME~s~ is your next destination.",
            "Head to ~y~PLACE_NAME~s~ to find a customer for your girl.",
            "Head to ~y~PLACE_NAME~s~ next.",
            "Take your girl near ~y~PLACE_NAME~s~.",
            "Head to ~y~PLACE_NAME~s~ for another trick for your girl.",
            "Drop your girl off at ~y~PLACE_NAME~s~ this time.",
        };

        public static readonly string[] ConversationSubtitleText =
        {
            "Nice wheels! Keep me safe and you'll get a percentage!",
            "Treat me right!",
        };

        public static readonly string[] PaymentSubtitleText =
        {
            "There's your share big boy!",
            "Here's your cut!",
        };

        public static readonly string[] NearCustomerSubtitleText =
        {
            "Ok! Here's my customer! Let me out and I'll call you when I'm done!",
            "Here's my man. Stop nearby. I'll call you when I'm ready for my next appointment.",
        };

        public static readonly string[] CustomerAttacksGirlSubtitleText =
        {
            "Help me! This punk is getting rough! He thinks I've given him crabs!",
        };

        public static readonly string[] CustomerDoesNotPaySubtitleText =
        {
            "I got a cheapskate who won't pay! I need your persuasive technique!",
        };

        public static readonly string[] AttackCustomerAttacksGirlSubtitleText =
        {
            "A ~r~customer~s~ is being too rough with your girl! Get him!",
            "That ~r~customer~s~ is taking advantage of your services. Destroy him!",
        };
        
        public static readonly string[] AttackCustomerDoesNotPaySubtitleText =
        {
            "Chase down that ~r~punk~s~ quickly!",
            "That ~r~customer~s~ is taking advantage of your services. Destroy him!",
        };

        public static readonly string[] CustomerKilledGoPickGirlSubtitleText =
        {
            "Good work! Drive back and pick up your ~b~girl~s~ to continue.",
            "Pick up your ~b~girl~s~ now!",
        };
        #endregion


        public static bool HasHelpTextInVehicleBeenShowed { get; private set; }
 
        public static bool ArePimpMissionsActive { get; private set; }

        private static PimpingState state;
        public static PimpingState State
        {
            get { return state; }
            private set { Game.LogTrivial("Changing PimpingState: from " + state + " to " + value); state = value; }
        }
        public static PimpingSituation PreviousSituation { get; private set; }
        private static PimpingSituation situation;
        public static PimpingSituation Situation
        {
            get { return situation; }
            private set { PreviousSituation = situation; Game.LogTrivial("Changing PimpingSituation: from " + situation + " to " + value); situation = value; }
        }

        public static Vehicle PimpVehicle { get; private set; }

        public static int CurrentLevel { get; private set; } = 0;

        public static Vector3 CustomerDestination { get; private set; }
        public static Blip CustomerDestinationBlip { get; private set; }

        public static Blip KillCustomerBlip { get; private set; }

        public static bool ShowPaymentWindow { get; private set; }
        public static bool ShowTimerText { get; private set; }
        public static TimeSpan TimerTime { get; private set; } = TimeSpan.FromSeconds(MissionTimeLimitInSeconds);

        public static void Main()
        {
            while (Game.IsLoading)
                GameFiber.Yield();

            Game.RawFrameRender += OnRawFrameRender;

            while (true)
            {
                GameFiber.Yield();
                MainUpdate();
            }
        }

        public static void MainUpdate()
        {
            Ped playerPed = Game.LocalPlayer.Character;

            if (playerPed)
            {
                if (!ArePimpMissionsActive)
                {
                    if (playerPed.IsInAnyVehicle(false) && playerPed.CurrentVehicle.Model == UserSettings.PimpVehicleModel)
                    {
                        if (!HasHelpTextInVehicleBeenShowed)
                        {
                            Game.DisplayHelp("Press ~INPUT_CONTEXT~ to start the pimp missions.");
                            HasHelpTextInVehicleBeenShowed = true;
                        }

                        if (Game.IsControlJustPressed(0, GameControl.Context))
                        {
                            Game.LogTrivial("Started pimp missions");
                            PimpVehicle = playerPed.CurrentVehicle;

                            Vector3 spawn;
                            float radius = SpawnMaxRadius;
                            while ((spawn = GetSafeCoordForPed(playerPed.Position.Around2D(radius))) == Vector3.Zero)
                            {
                                GameFiber.Yield();
                                radius += 10.0f;
                            }

                            SpawnFirstGirl(spawn, GetClosestVehicleNodeHeading(spawn) + 90.0f);
                            CreateFirstGirlBlip();
                            Game.DisplaySubtitle(FirstProstitutePickSubtitleText.GetRandomElement());

                            State = PimpingState.PickingFirstGirlFirstTime;
                            ArePimpMissionsActive = true;
                            Game.DisplayHelp("Hold ~INPUT_CONTEXT~ to end the pimp missions.");
                        }
                    }
                    else if (HasHelpTextInVehicleBeenShowed)
                    {
                        HasHelpTextInVehicleBeenShowed = false;
                    }
                }
                else
                {
                    if(Game.IsControlPressed(0, GameControl.Context))
                    {
                        contextControlHoldCounter++;
                        if(contextControlHoldCounter >= 200)
                        {
                            Game.LogTrivial("Ended pimp missions manually");
                            CleanUpAndEndCurrentSession();
                            ShowFailedMessageScaleform("You're no longer a pimp!", "PIMP MISSIONS ENDED");
                            contextControlHoldCounter = 0;
                            return;
                        }
                    }
                    else if(contextControlHoldCounter != 0)
                    {
                        contextControlHoldCounter = 0;
                    }

                    if(!playerPed || playerPed.IsDead)
                    {
                        Game.LogTrivial("Failed pimp missions: Player is dead or doesn't exist");
                        CleanUpAndEndCurrentSession();
                        return;
                    }

                    if(NativeFunction.Natives.IsPlayerBeingArrested<bool>(Game.LocalPlayer, false))
                    {
                        Game.LogTrivial("Failed pimp missions: Player is arrested");
                        CleanUpAndEndCurrentSession();
                        return;
                    }

                    if (!PimpVehicle || PimpVehicle.IsDead)
                    {
                        Game.LogTrivial("Failed pimp missions: Pimp vehicle is destroyed or doesn't exist");
                        GameFiber.StartNew(() => { ShowFailedMessageScaleform("~r~Your car is destroyed!~s~"); });
                        CleanUpAndEndCurrentSession();
                        return;
                    }

                    if((FirstGirl && FirstGirl.IsDead) || (SecondGirl && SecondGirl.IsDead) || (State >= PimpingState.PickedSecondGirlFirstTime && (!FirstGirl || !SecondGirl)))
                    {
                        Game.LogTrivial("Failed pimp mission: A girl is dead or doesn't exist");
                        GameFiber.StartNew(() => { ShowFailedMessageScaleform("~r~One of your girls is dead!~s~"); });
                        CleanUpAndEndCurrentSession();
                        return;
                    }

                    if (ShowTimerText)
                    {
                        if(TimerTime.TotalSeconds <= 0)
                        {
                            // 0 seconds left, player failed mission
                            Game.LogTrivial("Failed pimp mission: Run out of time");
                            GameFiber.StartNew(() => { ShowFailedMessageScaleform("~r~You run out of time!~s~"); });
                            CleanUpAndEndCurrentSession();
                            return;
                        }
                        else
                        {
                            // update timer
                            UpdateTimer();
                        }
                    }

                    if (!playerPed.IsInVehicle(PimpVehicle, false) && Situation == PimpingSituation.None)
                    {
                        Game.DisplaySubtitle("Go back to your ~b~vehicle~s~.", 10);
                        DrawMarker(MarkerType.UpsideDownCone, PimpVehicle.Position + Vector3.WorldUp * 2.5f, Vector3.Zero, new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.85f, 0.85f, 0.85f), Color.FromArgb(130, 93, 182, 229), true, false, 2, false, null, null, false);
                        return;
                    }


                    switch (State)
                    {
                        case PimpingState.None:
                            break;

                        case PimpingState.PickingFirstGirlFirstTime: // waits for player to be near the first girl, then she enters the player's vehicle and spawns customer
                            DrawMarker(MarkerType.UpsideDownCone, FirstGirl.Position + Vector3.WorldUp * 1.8f, Vector3.Zero, new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.675f, 0.675f, 0.675f), Color.FromArgb(130, 93, 182, 229), true, false, 2, false, null, null, false);

                            if (playerPed.DistanceTo(FirstGirl) < 6.0f && playerPed.Speed < 0.1f)
                            {
                                while (PimpVehicle && FirstGirl && FirstGirl.IsAlive && !FirstGirl.IsInVehicle(PimpVehicle, false))
                                {
                                    FirstGirl.Tasks.FollowNavigationMeshToPosition(PimpVehicle.GetOffsetPositionRight(2.5f), PimpVehicle.Heading + 90.0f, 1.0f, 3.0f).WaitForCompletion(10000);
                                    FirstGirl.Tasks.EnterVehicle(PimpVehicle, 0).WaitForCompletion(10000);
                                }
                                if (!PimpVehicle || !FirstGirl || FirstGirl.IsDead)
                                    return;

                                Vector3 customerSpawn;
                                float radius = SpawnMaxRadius;
                                while ((customerSpawn = GetSafeCoordForPed(playerPed.Position.Around2D(radius))) == Vector3.Zero)
                                {
                                    GameFiber.Yield();
                                    radius -= 10.0f;
                                }
                                SpawnFirstGirlCustomer(customerSpawn, GetClosestVehicleNodeHeading(customerSpawn) + 90.0f);

                                CustomerDestination = customerSpawn;
                                CustomerDestinationBlip = new Blip(CustomerDestination);

                                TimerTime = TimeSpan.FromSeconds(MissionTimeLimitInSeconds);
                                ShowTimerText = true;

                                GameFiber.StartNew(() =>
                                {
                                    Game.DisplaySubtitle(DestinationSubtitleText.GetRandomElement().Replace("PLACE_NAME", World.GetStreetName(customerSpawn)));
                                    GameFiber.Sleep(6000);
                                    Game.DisplaySubtitle(ConversationSubtitleText.GetRandomElement());
                                });
                                State = PimpingState.PickedFirstGirlFirstTime;
                            }
                            break;

                        case PimpingState.PickedFirstGirlFirstTime: // waits for player to be near the customer, then the first girl leaves and spawns the second girl
                            DrawMarker(MarkerType.UpsideDownCone, FirstGirlCustomer.Position + Vector3.WorldUp * 1.8f, Vector3.Zero, new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.675f, 0.675f, 0.675f), Color.FromArgb(130, 240, 200, 80), true, false, 2, false, null, null, false);

                            if (!FirstGirl.IsInVehicle(PimpVehicle, false))
                            {
                                while (PimpVehicle && FirstGirl && FirstGirl.IsAlive && !FirstGirl.IsInVehicle(PimpVehicle, false))
                                {
                                    FirstGirl.Tasks.FollowNavigationMeshToPosition(PimpVehicle.GetOffsetPositionRight(2.5f), PimpVehicle.Heading + 90.0f, 1.0f, 3.0f).WaitForCompletion(10000);
                                    FirstGirl.Tasks.EnterVehicle(PimpVehicle, 0).WaitForCompletion(10000);
                                }
                                if (!PimpVehicle || !FirstGirl || FirstGirl.IsDead)
                                    return;
                            }

                            if (PimpVehicle.DistanceTo(FirstGirlCustomer) < 10.0f)
                            {
                                if (!showedReachedCustomerMessage)
                                {
                                    Game.DisplaySubtitle(NearCustomerSubtitleText.GetRandomElement());
                                    showedReachedCustomerMessage = true;
                                }

                                if (PimpVehicle.Speed < 0.1f)
                                {
                                    ShowTimerText = false;
                                    while (FirstGirl && PimpVehicle && FirstGirl.IsAlive && FirstGirl.IsInVehicle(PimpVehicle, false))
                                    {
                                        FirstGirl.Tasks.LeaveVehicle(LeaveVehicleFlags.None).WaitForCompletion(10000);
                                    }
                                    if (!FirstGirl || !PimpVehicle || FirstGirl.IsDead)
                                        return;
                                    FirstGirl.Tasks.GoToOffsetFromEntity(FirstGirlCustomer, 1.25f, 0.0f, 1.0f);

                                    Vector3 spawn;
                                    float radius = SpawnMaxRadius;
                                    while ((spawn = GetSafeCoordForPed(playerPed.Position.Around2D(radius))) == Vector3.Zero)
                                    {
                                        GameFiber.Yield();
                                        radius -= 10.0f;
                                    }

                                    DeleteFirstGirlBlip();

                                    CustomerDestination = Vector3.Zero;
                                    if (CustomerDestinationBlip)
                                        CustomerDestinationBlip.Delete();

                                    SpawnSecondGirl(spawn, GetClosestVehicleNodeHeading(spawn) + 90.0f);
                                    CreateSecondGirlBlip();
                                    Game.DisplaySubtitle(SecondProstitutePickSubtitleText.GetRandomElement());

                                    showedReachedCustomerMessage = false;
                                    State = PimpingState.PickingSecondGirlFirstTime;
                                }
                            }
                            break;

                        case PimpingState.PickingSecondGirlFirstTime: // waits for player to be near the second girl, then she enters the player's vehicle and spawns customer
                            DrawMarker(MarkerType.UpsideDownCone, SecondGirl.Position + Vector3.WorldUp * 1.8f, Vector3.Zero, new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.675f, 0.675f, 0.675f), Color.FromArgb(130, 93, 182, 229), true, false, 2, false, null, null, false);

                            if (playerPed.DistanceTo(SecondGirl) < 6.0f && playerPed.Speed < 0.1f)
                            {
                                while (PimpVehicle && SecondGirl && SecondGirl.IsAlive &&!SecondGirl.IsInVehicle(PimpVehicle, false))
                                {
                                    SecondGirl.Tasks.FollowNavigationMeshToPosition(PimpVehicle.GetOffsetPositionRight(2.5f), PimpVehicle.Heading + 90.0f, 1.0f, 3.0f).WaitForCompletion(10000);
                                    SecondGirl.Tasks.EnterVehicle(PimpVehicle, 0).WaitForCompletion(10000);
                                }
                                if (!PimpVehicle || !SecondGirl || SecondGirl.IsDead)
                                    return;

                                Vector3 customerSpawn;
                                float radius = SpawnMaxRadius;
                                while ((customerSpawn = GetSafeCoordForPed(playerPed.Position.Around2D(radius))) == Vector3.Zero)
                                {
                                    GameFiber.Yield();
                                    radius -= 10.0f;
                                }
                                SpawnSecondGirlCustomer(customerSpawn, GetClosestVehicleNodeHeading(customerSpawn) + 90.0f);

                                CustomerDestination = customerSpawn;
                                CustomerDestinationBlip = new Blip(CustomerDestination);

                                TimerTime = TimeSpan.FromSeconds(MissionTimeLimitInSeconds);
                                ShowTimerText = true;

                                GameFiber.StartNew(() =>
                                {
                                    Game.DisplaySubtitle(DestinationSubtitleText.GetRandomElement().Replace("PLACE_NAME", World.GetStreetName(customerSpawn)));
                                    GameFiber.Sleep(6000);
                                    Game.DisplaySubtitle(ConversationSubtitleText.GetRandomElement());
                                });
                                State = PimpingState.PickedSecondGirlFirstTime;
                            }
                            break;

                        case PimpingState.PickedSecondGirlFirstTime: // waits for player to be near the customer, then the second girl leaves
                            DrawMarker(MarkerType.UpsideDownCone, SecondGirlCustomer.Position + Vector3.WorldUp * 1.8f, Vector3.Zero, new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.675f, 0.675f, 0.675f), Color.FromArgb(130, 240, 200, 80), true, false, 2, false, null, null, false);

                            if (!SecondGirl.IsInVehicle(PimpVehicle, false))
                            {
                                while (PimpVehicle && SecondGirl && SecondGirl.IsAlive && !SecondGirl.IsInVehicle(PimpVehicle, false))
                                {
                                    SecondGirl.Tasks.FollowNavigationMeshToPosition(PimpVehicle.GetOffsetPositionRight(2.5f), PimpVehicle.Heading + 90.0f, 1.0f, 3.0f).WaitForCompletion(10000);
                                    SecondGirl.Tasks.EnterVehicle(PimpVehicle, 0).WaitForCompletion(10000);
                                }
                                if (!PimpVehicle || !SecondGirl || SecondGirl.IsDead)
                                    return;
                            }

                            if (PimpVehicle.DistanceTo(SecondGirlCustomer) < 10.0f)
                            {
                                if (!showedReachedCustomerMessage)
                                {
                                    Game.DisplaySubtitle(NearCustomerSubtitleText.GetRandomElement());
                                    showedReachedCustomerMessage = true;
                                }

                                if (PimpVehicle.Speed < 0.1f)
                                {
                                    ShowTimerText = false;
                                    while (SecondGirl && PimpVehicle && SecondGirl.IsAlive && SecondGirl.IsInVehicle(PimpVehicle, false))
                                    {
                                        SecondGirl.Tasks.LeaveVehicle(LeaveVehicleFlags.None).WaitForCompletion(10000);
                                    }
                                    if (!SecondGirl || !PimpVehicle || SecondGirl.IsDead)
                                        return;
                                    SecondGirl.Tasks.GoToOffsetFromEntity(SecondGirlCustomer, 1.25f, 0.0f, 1.0f);

                                    Vector3 spawn;
                                    float radius = SpawnMaxRadius;
                                    while ((spawn = GetSafeCoordForPed(playerPed.Position.Around2D(radius))) == Vector3.Zero)
                                    {
                                        GameFiber.Yield();
                                        radius -= 10.0f;
                                    }

                                    DeleteSecondGirlBlip();

                                    CustomerDestination = Vector3.Zero;
                                    if (CustomerDestinationBlip)
                                        CustomerDestinationBlip.Delete();

                                    CreateFirstGirlBlip();
                                    Game.DisplaySubtitle(ProstitutePickSubtitleText.GetRandomElement());

                                    showedReachedCustomerMessage = false;
                                    State = PimpingState.PickingFirstGirl;
                                }
                            }
                            break;

                        // start mission levels loop
                        case PimpingState.PickingFirstGirl: // wait for player to pick the first girl, dismisses previous customer and spawns new one and give payment to player
                            if (Situation == PimpingSituation.None)
                                DrawMarker(MarkerType.UpsideDownCone, FirstGirl.Position + Vector3.WorldUp * 1.8f, Vector3.Zero, new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.675f, 0.675f, 0.675f), Color.FromArgb(130, 93, 182, 229), true, false, 2, false, null, null, false);

                            if (Situation == PimpingSituation.None && playerPed.DistanceTo(FirstGirl) < 6.0f && playerPed.Speed < 0.1f)
                            {
                                if (FirstGirlCustomer)
                                    FirstGirlCustomer.Dismiss();
                                while (PimpVehicle && FirstGirl && FirstGirl.IsAlive && !FirstGirl.IsInVehicle(PimpVehicle, false))
                                {
                                    FirstGirl.Tasks.FollowNavigationMeshToPosition(PimpVehicle.GetOffsetPositionRight(2.5f), PimpVehicle.Heading + 90.0f, 1.0f, 3.0f).WaitForCompletion(10000);
                                    FirstGirl.Tasks.EnterVehicle(PimpVehicle, 0).WaitForCompletion(10000);
                                }
                                if (!PimpVehicle || !FirstGirl || FirstGirl.IsDead)
                                    return;

                                Vector3 customerSpawn;
                                float radius = SpawnMaxRadius;
                                while ((customerSpawn = GetSafeCoordForPed(playerPed.Position.Around2D(radius))) == Vector3.Zero)
                                {
                                    GameFiber.Yield();
                                    radius -= 10.0f;
                                }
                                SpawnFirstGirlCustomer(customerSpawn, GetClosestVehicleNodeHeading(customerSpawn) + 90.0f);

                                CustomerDestination = customerSpawn;
                                CustomerDestinationBlip = new Blip(CustomerDestination);

                                TimerTime = TimeSpan.FromSeconds(MissionTimeLimitInSeconds);
                                ShowTimerText = true;

                                GameFiber.StartNew(() =>
                                {
                                    Game.DisplaySubtitle(DestinationSubtitleText.GetRandomElement().Replace("PLACE_NAME", World.GetStreetName(customerSpawn)));
                                    GameFiber.Sleep(6000);
                                    CurrentLevel++;
                                    if (PreviousSituation != PimpingSituation.CustomerDoesNotPay) // only give money to player if customer paid
                                    {
                                        SetPlayerMoney(GetPlayerMoney() + (MoneyRewardMultiplier * CurrentLevel));
                                        Game.DisplaySubtitle(PaymentSubtitleText.GetRandomElement());
                                        ShowPaymentWindow = true;
                                        GameFiber.Sleep(6000);
                                        ShowPaymentWindow = false;
                                    }
                                });
                                State = PimpingState.PickedFirstGirl;
                            }
                            else if(Situation == PimpingSituation.CustomerAttacksGirl || Situation == PimpingSituation.CustomerDoesNotPay) // player killed customer who attacked girl or didn't pay
                            {
                                if(!FirstGirlCustomer || FirstGirlCustomer.IsDead)
                                {
                                    Game.DisplaySubtitle(CustomerKilledGoPickGirlSubtitleText.GetRandomElement());
                                    ShowTimerText = false;
                                    DeleteKillCustomerBlip();
                                    Situation = PimpingSituation.None;
                                }
                                else
                                {
                                    DrawMarker(MarkerType.UpsideDownCone, FirstGirlCustomer.Position + Vector3.WorldUp * 1.8f, Vector3.Zero, new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.675f, 0.675f, 0.675f), Color.FromArgb(130, 224, 50, 50), true, false, 2, false, null, null, false);
                                }
                            }
                            break;

                        case PimpingState.PickedFirstGirl: // waits for the player to be near first girl customer, first girl leaves vehicle and indicates player to pick the second girl
                            DrawMarker(MarkerType.UpsideDownCone, FirstGirlCustomer.Position + Vector3.WorldUp * 1.8f, Vector3.Zero, new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.675f, 0.675f, 0.675f), Color.FromArgb(130, 240, 200, 80), true, false, 2, false, null, null, false);

                            if (!FirstGirl.IsInVehicle(PimpVehicle, false))
                            {
                                while (PimpVehicle && FirstGirl && FirstGirl.IsAlive && !FirstGirl.IsInVehicle(PimpVehicle, false))
                                {
                                    FirstGirl.Tasks.FollowNavigationMeshToPosition(PimpVehicle.GetOffsetPositionRight(2.5f), PimpVehicle.Heading + 90.0f, 1.0f, 3.0f).WaitForCompletion(10000);
                                    FirstGirl.Tasks.EnterVehicle(PimpVehicle, 0).WaitForCompletion(10000);
                                }
                                if (!PimpVehicle || !FirstGirl || FirstGirl.IsDead)
                                    return;
                            }

                            if (PimpVehicle.DistanceTo(FirstGirlCustomer) < 10.0f)
                            {
                                if (!showedReachedCustomerMessage)
                                {
                                    Game.DisplaySubtitle(NearCustomerSubtitleText.GetRandomElement());
                                    showedReachedCustomerMessage = true;
                                }

                                if (PimpVehicle.Speed < 0.1f)
                                {
                                    ShowTimerText = false;
                                    while (FirstGirl && PimpVehicle && FirstGirl.IsAlive && FirstGirl.IsInVehicle(PimpVehicle, false))
                                    {
                                        FirstGirl.Tasks.LeaveVehicle(LeaveVehicleFlags.None).WaitForCompletion(10000);
                                    }
                                    if (!FirstGirl || !PimpVehicle || FirstGirl.IsDead)
                                        return;
                                    FirstGirl.Tasks.GoToOffsetFromEntity(FirstGirlCustomer, 1.25f, 0.0f, 1.0f);

                                    DeleteFirstGirlBlip();

                                    CustomerDestination = Vector3.Zero;
                                    if (CustomerDestinationBlip)
                                        CustomerDestinationBlip.Delete();

                                    CreateSecondGirlBlip();

                                    Situation = Random.Next(101) < 65 ? (Random.Next(2) == 0 ? PimpingSituation.CustomerAttacksGirl : PimpingSituation.CustomerDoesNotPay) : PimpingSituation.None;
                                    if (Situation == PimpingSituation.None)
                                    {
                                        Game.DisplaySubtitle(ProstitutePickSubtitleText.GetRandomElement());
                                    }
                                    else if(Situation == PimpingSituation.CustomerAttacksGirl)
                                    {
                                        SecondGirl.Health = SecondGirl.MaxHealth; // heal girl
                                        CreateKillCustomerBlip(SecondGirlCustomer);
                                        Game.DisplaySubtitle(CustomerAttacksGirlSubtitleText.GetRandomElement());
                                        SecondGirlCustomer.Tasks.FightAgainst(SecondGirl, -1);
                                        SecondGirl.Tasks.FightAgainst(SecondGirlCustomer, -1);
                                        GameFiber.StartNew(() =>
                                        {
                                            GameFiber.Sleep(5000);
                                            Game.DisplaySubtitle(AttackCustomerAttacksGirlSubtitleText.GetRandomElement());
                                        });
                                        TimerTime = TimeSpan.FromSeconds(MissionTimeLimitInSeconds);
                                        ShowTimerText = true;
                                    }
                                    else if(Situation == PimpingSituation.CustomerDoesNotPay)
                                    {
                                        CreateKillCustomerBlip(SecondGirlCustomer);
                                        Game.DisplaySubtitle(CustomerDoesNotPaySubtitleText.GetRandomElement());
                                        SecondGirlCustomer.Tasks.ReactAndFlee(SecondGirl);
                                        GameFiber.StartNew(() =>
                                        {
                                            GameFiber.Sleep(5000);
                                            Game.DisplaySubtitle(AttackCustomerDoesNotPaySubtitleText.GetRandomElement());
                                        });
                                        TimerTime = TimeSpan.FromSeconds(MissionTimeLimitInSeconds);
                                        ShowTimerText = true;
                                    }

                                    showedReachedCustomerMessage = false;
                                    State = PimpingState.PickingSecondGirl;
                                }
                            }
                            break;

                        case PimpingState.PickingSecondGirl: // wait for player to pick the second girl, dismisses previous customer and spawns new one and give payment to player
                            if (Situation == PimpingSituation.None)
                                DrawMarker(MarkerType.UpsideDownCone, SecondGirl.Position + Vector3.WorldUp * 1.8f, Vector3.Zero, new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.675f, 0.675f, 0.675f), Color.FromArgb(130, 93, 182, 229), true, false, 2, false, null, null, false);

                            if (Situation == PimpingSituation.None && playerPed.DistanceTo(SecondGirl) < 6.0f && playerPed.Speed < 0.1f)
                            {
                                if (SecondGirlCustomer)
                                    SecondGirlCustomer.Dismiss();
                                while (PimpVehicle && SecondGirl && SecondGirl.IsAlive && !SecondGirl.IsInVehicle(PimpVehicle, false))
                                {
                                    SecondGirl.Tasks.FollowNavigationMeshToPosition(PimpVehicle.GetOffsetPositionRight(2.5f), PimpVehicle.Heading + 90.0f, 1.0f, 3.0f).WaitForCompletion(10000);
                                    SecondGirl.Tasks.EnterVehicle(PimpVehicle, 0).WaitForCompletion(10000);
                                }
                                if (!PimpVehicle || !SecondGirl || SecondGirl.IsDead)
                                    return;
                                Vector3 customerSpawn;
                                float radius = SpawnMaxRadius;
                                while ((customerSpawn = GetSafeCoordForPed(playerPed.Position.Around2D(radius))) == Vector3.Zero)
                                {
                                    GameFiber.Yield();
                                    radius -= 10.0f;
                                }
                                SpawnSecondGirlCustomer(customerSpawn, GetClosestVehicleNodeHeading(customerSpawn) + 90.0f);

                                CustomerDestination = customerSpawn;
                                CustomerDestinationBlip = new Blip(CustomerDestination);

                                TimerTime = TimeSpan.FromSeconds(MissionTimeLimitInSeconds);
                                ShowTimerText = true;

                                GameFiber.StartNew(() =>
                                {
                                    Game.DisplaySubtitle(DestinationSubtitleText.GetRandomElement().Replace("PLACE_NAME", World.GetStreetName(customerSpawn)));
                                    GameFiber.Sleep(6000);
                                    CurrentLevel++;
                                    if (PreviousSituation != PimpingSituation.CustomerDoesNotPay) // only give money to player if customer paid
                                    {
                                        SetPlayerMoney(GetPlayerMoney() + (MoneyRewardMultiplier * CurrentLevel));
                                        Game.DisplaySubtitle(PaymentSubtitleText.GetRandomElement());
                                        ShowPaymentWindow = true;
                                        GameFiber.Sleep(6000);
                                        ShowPaymentWindow = false;
                                    }
                                });
                                State = PimpingState.PickedSecondGirl;
                            }
                            else if (Situation == PimpingSituation.CustomerAttacksGirl || Situation == PimpingSituation.CustomerDoesNotPay)
                            {
                                if (!SecondGirlCustomer || SecondGirlCustomer.IsDead) // player killed customer who attacked girl or didn't pay
                                {
                                    Game.DisplaySubtitle(CustomerKilledGoPickGirlSubtitleText.GetRandomElement());
                                    ShowTimerText = false;
                                    DeleteKillCustomerBlip();
                                    Situation = PimpingSituation.None;
                                }
                                else
                                {
                                    DrawMarker(MarkerType.UpsideDownCone, SecondGirlCustomer.Position + Vector3.WorldUp * 1.8f, Vector3.Zero, new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.675f, 0.675f, 0.675f), Color.FromArgb(130, 224, 50, 50), true, false, 2, false, null, null, false);
                                }
                            }
                            break;

                        case PimpingState.PickedSecondGirl: // waits for the player to be near second girl customer, second girl leaves vehicle and indicates player to pick the first girl, repeat PickingFirstGirl
                            DrawMarker(MarkerType.UpsideDownCone, SecondGirlCustomer.Position + Vector3.WorldUp * 1.8f, Vector3.Zero, new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.675f, 0.675f, 0.675f), Color.FromArgb(130, 240, 200, 80), true, false, 2, false, null, null, false);

                            if(!SecondGirl.IsInVehicle(PimpVehicle, false))
                            {
                                while (PimpVehicle && SecondGirl && SecondGirl.IsAlive && !SecondGirl.IsInVehicle(PimpVehicle, false))
                                {
                                    SecondGirl.Tasks.FollowNavigationMeshToPosition(PimpVehicle.GetOffsetPositionRight(2.5f), PimpVehicle.Heading + 90.0f, 1.0f, 3.0f).WaitForCompletion(10000);
                                    SecondGirl.Tasks.EnterVehicle(PimpVehicle, 0).WaitForCompletion(10000);
                                }
                                if (!PimpVehicle || !SecondGirl || SecondGirl.IsDead)
                                    return;
                            }

                            if (PimpVehicle.DistanceTo(SecondGirlCustomer) < 10.0f)
                            {
                                if (!showedReachedCustomerMessage)
                                {
                                    Game.DisplaySubtitle(NearCustomerSubtitleText.GetRandomElement());
                                    showedReachedCustomerMessage = true;
                                }

                                if (PimpVehicle.Speed < 0.1f)
                                {
                                    ShowTimerText = false;
                                    while (SecondGirl && PimpVehicle && SecondGirl.IsAlive && SecondGirl.IsInVehicle(PimpVehicle, false))
                                    {
                                        SecondGirl.Tasks.LeaveVehicle(LeaveVehicleFlags.None).WaitForCompletion(10000);
                                    }
                                    if (!SecondGirl || !PimpVehicle || SecondGirl.IsDead)
                                        return;
                                    SecondGirl.Tasks.GoToOffsetFromEntity(SecondGirlCustomer, 1.25f, 0.0f, 1.0f);

                                    DeleteSecondGirlBlip();

                                    CustomerDestination = Vector3.Zero;
                                    if (CustomerDestinationBlip)
                                        CustomerDestinationBlip.Delete();

                                    CreateFirstGirlBlip();

                                    Situation = Random.Next(101) < 65 ? (Random.Next(2) == 0 ? PimpingSituation.CustomerAttacksGirl : PimpingSituation.CustomerDoesNotPay) : PimpingSituation.None;
                                    if (Situation == PimpingSituation.None)
                                    {
                                        Game.DisplaySubtitle(ProstitutePickSubtitleText.GetRandomElement());
                                    }
                                    else if (Situation == PimpingSituation.CustomerAttacksGirl)
                                    {
                                        FirstGirl.Health = FirstGirl.MaxHealth; // heal girl
                                        CreateKillCustomerBlip(FirstGirlCustomer);
                                        Game.DisplaySubtitle(CustomerAttacksGirlSubtitleText.GetRandomElement());
                                        FirstGirlCustomer.Tasks.FightAgainst(FirstGirl, -1);
                                        FirstGirl.Tasks.FightAgainst(FirstGirlCustomer, -1);
                                        GameFiber.StartNew(() => 
                                        {
                                            GameFiber.Sleep(5000);
                                            Game.DisplaySubtitle(AttackCustomerAttacksGirlSubtitleText.GetRandomElement());
                                        });
                                        TimerTime = TimeSpan.FromSeconds(MissionTimeLimitInSeconds);
                                        ShowTimerText = true;
                                    }
                                    else if (Situation == PimpingSituation.CustomerDoesNotPay)
                                    {
                                        CreateKillCustomerBlip(FirstGirlCustomer);
                                        Game.DisplaySubtitle(CustomerDoesNotPaySubtitleText.GetRandomElement());
                                        FirstGirlCustomer.Tasks.ReactAndFlee(FirstGirl);
                                        GameFiber.StartNew(() =>
                                        {
                                            GameFiber.Sleep(5000);
                                            Game.DisplaySubtitle(AttackCustomerDoesNotPaySubtitleText.GetRandomElement());
                                        });
                                        TimerTime = TimeSpan.FromSeconds(MissionTimeLimitInSeconds);
                                        ShowTimerText = true;
                                    }

                                    showedReachedCustomerMessage = false;
                                    State = PimpingState.PickingFirstGirl;
                                }
                            }
                            break;
                    }
                }
            }
        }

        static int contextControlHoldCounter = 0;

        static bool showedReachedCustomerMessage = false;

        public static Ped FirstGirl { get; private set; }
        public static Ped SecondGirl { get; private set; }

        public static Blip FirstGirlBlip { get; private set; }
        public static Blip SecondGirlBlip { get; private set; }

        public static Ped FirstGirlCustomer { get; private set; }
        public static Ped SecondGirlCustomer { get; private set; }

        public static void SpawnFirstGirl(Vector3 position, float heading)
        {
            DeleteFirstGirl();
            FirstGirl = new Ped(ProstitutesPedsModels.GetRandomElement(), position, heading);
            FirstGirl.RelationshipGroup = GirlsRelationshipGroup;
            Game.SetRelationshipBetweenRelationshipGroups(GirlsRelationshipGroup, "PLAYER", Relationship.Companion);
            FirstGirl.RandomizeVariation();
            FirstGirl.MaxHealth += 115;
            FirstGirl.Health = FirstGirl.MaxHealth;
            FirstGirl.BlockPermanentEvents = true;
        }

        public static void SpawnSecondGirl(Vector3 position, float heading)
        {
            DeleteSecondGirl();
            SecondGirl = new Ped(ProstitutesPedsModels.GetRandomElement(), position, heading);
            SecondGirl.RelationshipGroup = GirlsRelationshipGroup;
            Game.SetRelationshipBetweenRelationshipGroups(GirlsRelationshipGroup, "PLAYER", Relationship.Companion);
            SecondGirl.RandomizeVariation();
            SecondGirl.MaxHealth += 115;
            SecondGirl.Health = SecondGirl.MaxHealth;
            SecondGirl.BlockPermanentEvents = true;
        }

        public static void DeleteFirstGirl()
        {
            if (FirstGirl)
                FirstGirl.Delete();
        }

        public static void DeleteSecondGirl()
        {
            if (SecondGirl)
                SecondGirl.Delete();
        }

        public static void CreateFirstGirlBlip()
        {
            DeleteFirstGirlBlip();
            FirstGirlBlip = new Blip(FirstGirl);
            FirstGirlBlip.Color = Color.FromArgb(93, 182, 229);
            FirstGirlBlip.Name = "Girl";
        }

        public static void CreateSecondGirlBlip()
        {
            DeleteSecondGirlBlip();
            SecondGirlBlip = new Blip(SecondGirl);
            SecondGirlBlip.Color = Color.FromArgb(93, 182, 229);
            SecondGirlBlip.Name = "Girl";
        }

        public static void DeleteFirstGirlBlip()
        {
            if (FirstGirlBlip)
                FirstGirlBlip.Delete();
        }

        public static void DeleteSecondGirlBlip()
        {
            if (SecondGirlBlip)
                SecondGirlBlip.Delete();
        }

        public static void SpawnFirstGirlCustomer(Vector3 position, float heading)
        {
            if (FirstGirlCustomer)
                FirstGirlCustomer.Dismiss();
            FirstGirlCustomer = new Ped(CustomersMalePedsModels.GetRandomElement(), position, heading);
            FirstGirlCustomer.RandomizeVariation();
            FirstGirlCustomer.BlockPermanentEvents = true;
        }

        public static void SpawnSecondGirlCustomer(Vector3 position, float heading)
        {
            if (SecondGirlCustomer)
                SecondGirlCustomer.Dismiss();
            SecondGirlCustomer = new Ped(CustomersMalePedsModels.GetRandomElement(), position, heading);
            SecondGirlCustomer.RandomizeVariation();
            SecondGirlCustomer.BlockPermanentEvents = true;
        }

        public static void DeleteFirstGirlCustomer()
        {
            if (FirstGirlCustomer)
                FirstGirlCustomer.Delete();
        }

        public static void DeleteSecondGirlCustomer()
        {
            if (SecondGirlCustomer)
                SecondGirlCustomer.Delete();
        }

        public static void CreateKillCustomerBlip(Ped customer)
        {
            DeleteKillCustomerBlip();
            KillCustomerBlip = new Blip(customer);
            KillCustomerBlip.IsFriendly = false;
            KillCustomerBlip.Name = "Customer";
        }

        public static void DeleteKillCustomerBlip()
        {
            if (KillCustomerBlip)
                KillCustomerBlip.Delete();
        }

        // reduce the timer in one second every second
        static DateTime PreviousTimerUpdate = DateTime.UtcNow;
        static double SecondsToSubstractInTimerUpdate = 1.0;
        public static void UpdateTimer()
        {
            if((DateTime.UtcNow - PreviousTimerUpdate).TotalSeconds >= SecondsToSubstractInTimerUpdate)
            {
                TimerTime -= TimeSpan.FromSeconds(SecondsToSubstractInTimerUpdate);
                PreviousTimerUpdate = DateTime.UtcNow;
            }
        }

        static uint FailedMessageScaleformHandle;
        public static void ShowFailedMessageScaleform(string description, string title = "~r~PIMP MISSIONS FAILED~s~")
        {
            NativeFunction.Natives.RequestScriptAudioBank<bool>("generic_failed", false);

            if (FailedMessageScaleformHandle == 0)
            {
                FailedMessageScaleformHandle = NativeFunction.Natives.RequestScaleformMovie<uint>("MP_BIG_MESSAGE_FREEMODE");

                const int timeout = 1000;
                DateTime _start = DateTime.UtcNow;
                while (!NativeFunction.Natives.HasScaleformMovieLoaded<bool>(FailedMessageScaleformHandle) && DateTime.UtcNow.Subtract(_start).TotalMilliseconds < timeout)
                    GameFiber.Yield();
            }

            NativeFunction.Natives.xf6e48914c7a8694e(FailedMessageScaleformHandle, "SHOW_SHARD_CENTERED_MP_MESSAGE"); //PushScaleformMovieFunction
            NativeFunction.Natives.x80338406f3475e55("STRING"); // BeginTextComponent
            NativeFunction.Natives.x6c188be134e074aa(title); // AddTextComponentString
            NativeFunction.Natives.x362e2d3fe93a9959(); // EndTextComponent
            NativeFunction.Natives.x80338406f3475e55("STRING"); // BeginTextComponent
            NativeFunction.Natives.x6c188be134e074aa(description); // AddTextComponentString
            NativeFunction.Natives.x362e2d3fe93a9959(); // EndTextComponent
            NativeFunction.Natives.xc3d0841a0cc546a6(12); // background // PushScaleformMovieFunctionParameterInt
            NativeFunction.Natives.xc3d0841a0cc546a6(2); // text // PushScaleformMovieFunctionParameterInt
            NativeFunction.Natives.xc6796a8ffa375e53(); // PopScaleformMovieFunctionVoid

            NativeFunction.Natives.PlaySoundFrontend(-1, "ScreenFlash", "MissionFailedSounds", 1);

            uint start = Game.GameTime;
            int time = 5000;
            while (Game.GameTime - start < time)
            {
                GameFiber.Yield();
                NativeFunction.Natives.x0df606929c105be1(FailedMessageScaleformHandle, 255, 255, 255, 255); // DrawScaleformMovieDefault
            }

            NativeFunction.Natives.xf6e48914c7a8694e(FailedMessageScaleformHandle, "TRANSITION_OUT"); // PushScaleformMovieFunction
            NativeFunction.Natives.xc6796a8ffa375e53(); // PopScaleformMovieFunctionVoid

            start = Game.GameTime;
            time = 2000;
            while (Game.GameTime - start < time)
            {
                GameFiber.Yield();
                NativeFunction.Natives.x0df606929c105be1(FailedMessageScaleformHandle, 255, 255, 255, 255); // DrawScaleformMovieDefault
            }
            DisposeFailedMessageScaleform();
        }

        public static void DisposeFailedMessageScaleform()
        {
            if (FailedMessageScaleformHandle == 0)
                return;
            NativeFunction.Natives.SetScaleformMovieAsNoLongerNeeded(ref FailedMessageScaleformHandle);
        }

        public static void CleanUpAndEndCurrentSession()
        {
            ShowTimerText = false;
            ShowPaymentWindow = false;
            ArePimpMissionsActive = false;
            CurrentLevel = 0;
            State = PimpingState.None;
            DeleteFirstGirlBlip();
            DeleteSecondGirlBlip();
            DeleteKillCustomerBlip();
            if (CustomerDestinationBlip)
                CustomerDestinationBlip.Delete();
            CustomerDestination = Vector3.Zero;
            if (FirstGirl)
                FirstGirl.Dismiss();
            if (SecondGirl)
                SecondGirl.Dismiss();
            if (FirstGirlCustomer)
                FirstGirlCustomer.Dismiss();
            if (SecondGirlCustomer)
                SecondGirlCustomer.Dismiss();
        }

        public static void FinalizerCleanUp()
        {
            DisposeFailedMessageScaleform();
            DeleteFirstGirlBlip();
            DeleteSecondGirlBlip();
            DeleteKillCustomerBlip();
            DeleteFirstGirl();
            DeleteSecondGirl();
            DeleteFirstGirlCustomer();
            DeleteSecondGirlCustomer();
            if (CustomerDestinationBlip)
                CustomerDestinationBlip.Delete();
        }


        static readonly PointF TimerTextPoint = ConvertToCurrentCoordSystem(new PointF(1920 - 240, 110));
        static readonly string TimerTextFontName = "Adobe Gothic Std";
        static readonly float TimerTextFontSize = ScaleFontSizeToCurrentResolution(30.0f);

        static readonly RectangleF PaymentWindowBackgroundRectangle = ConvertToCurrentCoordSystem(new RectangleF(20, 200, 350, 240));
        static readonly Color PaymentWindowBackgroundColor = Color.FromArgb(200, Color.Black);
        static readonly PointF PaymentWindowTitlePoint = ConvertToCurrentCoordSystem(new PointF(35, 160));
        static readonly string PaymentWindowTitleFontName = "Magneto";
        static readonly float PaymentWindowTitleFontSize = ScaleFontSizeToCurrentResolution(55.0f);
        static readonly string PaymentWindowTextFontName = "Adobe Gothic Std";
        static readonly float PaymentWindowTextFontSize = ScaleFontSizeToCurrentResolution(28.25f);
        static readonly PointF PaymentWindowText01Point = ConvertToCurrentCoordSystem(new PointF(40, 230));
        static readonly PointF PaymentWindowText02Point = ConvertToCurrentCoordSystem(new PointF(40, 265));
        static readonly PointF PaymentWindowText03Point = ConvertToCurrentCoordSystem(new PointF(40, 295));
        static readonly PointF PaymentWindowText04Point = ConvertToCurrentCoordSystem(new PointF(40, 325));

        static void OnRawFrameRender(object sender, GraphicsEventArgs e)
        {
            if (ShowTimerText || Game.IsKeyDownRightNow(System.Windows.Forms.Keys.U))
            {
                // if remaining time is less than 30 second text color changes between red and white
                e.Graphics.DrawText("TIME            " + TimerTime.ToString(@"mm\:ss"), TimerTextFontName, TimerTextFontSize, TimerTextPoint, TimerTime.TotalSeconds < 30 ? (DateTime.UtcNow.Second % 2 == 0 ? Color.FromArgb(200, 10, 10) : Color.White) : Color.White);
            }

            if (ShowPaymentWindow || Game.IsKeyDownRightNow(System.Windows.Forms.Keys.Y))
            {
                e.Graphics.DrawRectangle(PaymentWindowBackgroundRectangle, PaymentWindowBackgroundColor);

                e.Graphics.DrawText("Pimping", PaymentWindowTitleFontName, PaymentWindowTitleFontSize, PaymentWindowTitlePoint, Color.White);

                e.Graphics.DrawText("Payment:", PaymentWindowTextFontName, PaymentWindowTextFontSize, PaymentWindowText01Point, Color.White);
                e.Graphics.DrawText("Trick Cut:     $" + MoneyRewardMultiplier, PaymentWindowTextFontName, PaymentWindowTextFontSize, PaymentWindowText02Point, Color.LightBlue);
                e.Graphics.DrawText("Multiplier:    X " + CurrentLevel, PaymentWindowTextFontName, PaymentWindowTextFontSize, PaymentWindowText03Point, Color.LightBlue);
                e.Graphics.DrawText("Total Cash:    $" + (MoneyRewardMultiplier * CurrentLevel), PaymentWindowTextFontName, PaymentWindowTextFontSize, PaymentWindowText04Point, Color.LightBlue);
            }
        }

        public enum PimpingState
        {
            None = 0,

            PickingFirstGirlFirstTime = 1,
            PickedFirstGirlFirstTime = 2,

            PickingSecondGirlFirstTime = 3,
            PickedSecondGirlFirstTime = 4,


            PickingFirstGirl = 5,
            PickedFirstGirl = 6,

            PickingSecondGirl = 7,
            PickedSecondGirl = 8,
        }

        public enum PimpingSituation
        {
            None = 0,
            CustomerAttacksGirl = 1,
            CustomerDoesNotPay = 2,
        }

        public static Vector3 GetSafeCoordForPed(Vector3 position, bool sidewalk = false, int flags =  1 << 4)
        {
            Vector3 p = World.GetNextPositionOnStreet(position);
            Vector3 outPos;
            const int MaxAttempts = 30;
            int attemps = 0;
            while (!NativeFunction.Natives.GetSafeCoordForPed<bool>(p.X, p.Y, p.Z, sidewalk, out outPos, flags))
            {
                GameFiber.Yield();
                p.X += 2f;
                attemps++;
                if (attemps > MaxAttempts)
                    break;
            }
            return outPos;
        }

        public static float GetClosestVehicleNodeHeading(Vector3 position)
        {
            float outHeading;
            Vector3 outPosition;

            NativeFunction.Natives.GetClosestVehicleNodeWithHeading(position.X, position.Y, position.Z, out outPosition, out outHeading, 12, 0x40400000, 0);

            return outHeading;
        }

        public static T GetRandomElement<T>(this IList<T> list)
        {
            if (list == null || list.Count <= 0)
                return default(T);
            
            return list[Random.Next(list.Count)];
        }

        public static T GetRandomElement<T>(this IEnumerable<T> enumarable, bool shuffle = false)
        {
            if (enumarable == null || enumarable.Count() <= 0)
                return default(T);

            T[] array = enumarable.ToArray();
            return GetRandomElement(array, shuffle);
        }

        public static void DrawMarker(MarkerType type, Vector3 pos, Vector3 dir, Vector3 rot, Vector3 scale, Color color)
        {
            DrawMarker(type, pos, dir, rot, scale, color, false, false, 2, false, null, null, false);
        }
        public static void DrawMarker(MarkerType type, Vector3 pos, Vector3 dir, Vector3 rot, Vector3 scale, Color color, bool bobUpAndDown, bool faceCamY, int unk2, bool rotateY, string textueDict, string textureName, bool drawOnEnt)
        {
            dynamic dict = 0;
            dynamic name = 0;

            if (textueDict != null && textureName != null)
            {
                if (textueDict.Length > 0 && textureName.Length > 0)
                {
                    dict = textueDict;
                    name = textureName;
                }
            }
            NativeFunction.Natives.DrawMarker((int)type, pos.X, pos.Y, pos.Z, dir.X, dir.Y, dir.Z, rot.X, rot.Y, rot.Z, scale.X, scale.Y, scale.Z, (int)color.R, (int)color.G, (int)color.B, (int)color.A, bobUpAndDown, faceCamY, unk2, rotateY, dict, name, drawOnEnt);
        }

        public static int GetPlayerMoney()
        {
            uint stat;

            switch (Game.LocalPlayer.Model.Hash)
            {
                case 0x0D7114C9/*player_zero*/:
                    stat = Game.GetHashKey("SP0_TOTAL_CASH");
                    break;
                case 0x9B22DBAF/*player_one*/:
                    stat = Game.GetHashKey("SP1_TOTAL_CASH");
                    break;
                case 0x9B810FA2/*player_two*/:
                    stat = Game.GetHashKey("SP2_TOTAL_CASH");
                    break;
                default:
                    return 0;
            }

            int result;
            NativeFunction.Natives.StatGetInt(stat, out result, -1);

            return result;
        }

        public static void SetPlayerMoney(int value)
        {
            uint stat;

            switch (Game.LocalPlayer.Model.Hash)
            {
                case 0x0D7114C9/*player_zero*/:
                    stat = Game.GetHashKey("SP0_TOTAL_CASH");
                    break;
                case 0x9B22DBAF/*player_one*/:
                    stat = Game.GetHashKey("SP1_TOTAL_CASH");
                    break;
                case 0x9B810FA2/*player_two*/:
                    stat = Game.GetHashKey("SP2_TOTAL_CASH");
                    break;
                default:
                    return;
            }
            
            NativeFunction.Natives.StatSetInt(stat, value, -1);
        }
        
        public static RectangleF ConvertToCurrentCoordSystem(RectangleF rectangle)
        {
            Size origRes = Game.Resolution;
            float aspectRatio = origRes.Width / (float)origRes.Height;
            PointF pos = new PointF(rectangle.X / (1080 * aspectRatio), rectangle.Y / 1080f);
            SizeF siz = new SizeF(rectangle.Width / (1080 * aspectRatio), rectangle.Height / 1080f);
            return new RectangleF(pos.X * Game.Resolution.Width, pos.Y * Game.Resolution.Height, siz.Width * Game.Resolution.Width, siz.Height * Game.Resolution.Height);
        }
        
        public static PointF ConvertToCurrentCoordSystem(PointF point)
        {
            Size origRes = Game.Resolution;
            float aspectRatio = origRes.Width / (float)origRes.Height;
            PointF pos = new PointF(point.X / (1080 * aspectRatio), point.Y / 1080f);
            return new PointF(pos.X * Game.Resolution.Width, pos.Y * Game.Resolution.Height);
        }

        public static float ScaleFontSizeToCurrentResolution(float fontSize)
        {
            const float LargerFontFactor = 1.5f;
            const float SmallerFontFactor = 0.8f;

            if (Game.Resolution.Height == 1080)
                return fontSize;

            float scaleFactor = Game.Resolution.Height < 1080 ? SmallerFontFactor : LargerFontFactor;

            return fontSize * scaleFactor;
        }
    }

    internal struct Settings
    {
        public readonly InitializationFile INIFile;

        /// <summary>
        /// The vehicle model for the pimp missions.
        /// </summary>
        public readonly Model PimpVehicleModel;

        //public readonly float SpawnRadius;

        public Settings(string iniFilePath)
        {
            INIFile = new InitializationFile(iniFilePath, false);

            PimpVehicleModel = INIFile.ReadString("PimpingV", "Pimp's Vehicle Model", "buccaneer2");
            //SpawnRadius = INIFile.ReadSingle("TEST", "Spawn Radius", 240.0f);
        }
    }

    internal enum MarkerType
    {
        UpsideDownCone = 0,
        VerticalCylinder = 1,
        ThickChevronUp = 2,
        ThinChevronUp = 3,
        CheckeredFlagRect = 4,
        CheckeredFlagCircle = 5,
        VerticleCircle = 6,
        PlaneModel = 7,
        LostMCDark = 8,
        LostMCLight = 9,
        Number0 = 10,
        Number1 = 11,
        Number2 = 12,
        Number3 = 13,
        Number4 = 14,
        Number5 = 15,
        Number6 = 16,
        Number7 = 17,
        Number8 = 18,
        Number9 = 19,
        ChevronUpx1 = 20,
        ChevronUpx2 = 21,
        ChevronUpx3 = 22,
        HorizontalCircleFat = 23,
        ReplayIcon = 24,
        HorizontalCircleSkinny = 25,
        HorizontalCircleSkinny_Arrow = 26,
        HorizontalSplitArrowCircle = 27,
        DebugSphere = 28
    }
}

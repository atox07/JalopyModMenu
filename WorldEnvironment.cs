using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JalopyModMenu
{
    internal enum WeatherMode { Automatic = -1, Sunny, Fog, Rain, Storm }

    internal sealed class WorldEnvironment
    {
        private const float SecondsPerHour = 3600f;
        private readonly GameBindings game;
        private readonly Type directorType;
        private readonly Type routeType;
        private readonly Type clockType;
        private readonly Type soundType;
        private readonly Type carType;
        private readonly Type performanceType;
        private readonly Type seatType;
        private readonly FieldInfo transitionEnabled;
        private readonly FieldInfo transitionStart;
        private readonly FieldInfo transitionDuration;
        private readonly FieldInfo clockSeconds;
        private readonly MethodInfo environmentFromTransition;
        private readonly MethodInfo applyEnvironment;
        private readonly MethodInfo applyTransition;
        private readonly System.Random lightningRandom = new System.Random();
        private Component frozenClock;
        private Component weatherRoute;
        private WeatherMode forcedWeather = WeatherMode.Automatic;
        private WeatherMode naturalWeather;
        private Light lightning;
        private float nextFlash;
        private float flashStart = float.NegativeInfinity;

        public readonly MethodInfo ClockUpdate;
        public readonly MethodInfo NativeSetWeather;

        public WorldEnvironment(GameBindings game)
        {
            this.game = game;
            var scripts = Assembly.Load("Assembly-CSharp");
            var firstPass = Assembly.Load("Assembly-CSharp-firstpass");
            directorType = scripts.GetType("DirectorC", true);
            routeType = scripts.GetType("RouteGeneratorC", true);
            carType = scripts.GetType("CarLogicC", true);
            performanceType = scripts.GetType("CarPerformanceC", true);
            seatType = scripts.GetType("SeatLogicC", true);
            clockType = firstPass.GetType("DNC_DayNight", true);
            soundType = firstPass.GetType("DNC_soundEffect", true);
            var transition = firstPass.GetType("TimeOfDayTransition", true);
            transitionEnabled = AccessTools.Field(transition, "enabled");
            transitionStart = AccessTools.Field(transition, "startHour");
            transitionDuration = AccessTools.Field(transition, "durationInHours");
            clockSeconds = AccessTools.Field(clockType, "_timeInSeconds");
            environmentFromTransition = AccessTools.Method(clockType, "GetEnvironmentStateFromTransition");
            applyEnvironment = AccessTools.Method(clockType, "ApplyEnvironmentState");
            applyTransition = AccessTools.Method(clockType, "ApplyCurrentTransition");
            ClockUpdate = AccessTools.Method(clockType, "Update");
            NativeSetWeather = AccessTools.Method(routeType, "SetWeather");
        }

        private GameObject DirectorObject
        {
            get
            {
                var menu = game.Menu;
                return menu ? GameTools.Read<GameObject>(menu, "director") : null;
            }
        }

        private Component Route
        {
            get
            {
                var director = DirectorObject;
                return director ? director.GetComponent(routeType) : null;
            }
        }

        private Component Clock(Component route)
        {
            GameObject dayCycle = route ? GameTools.Read<GameObject>(route, "uDayCycle") : null;
            if (!dayCycle)
            {
                var directorObject = DirectorObject;
                var director = directorObject ? directorObject.GetComponent(directorType) : null;
                dayCycle = director ? GameTools.Read<GameObject>(director, "dayNightCycle") : null;
            }
            return dayCycle ? dayCycle.GetComponent(clockType) : null;
        }

        public string TimeLabel()
        {
            var clock = Clock(Route);
            if (!clock)
                return "--:--";
            int minutes = Mathf.FloorToInt(Mathf.Repeat((float)clockSeconds.GetValue(clock), 86400f) / 60f);
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }

        public bool IsFrozen { get { return frozenClock && frozenClock == Clock(Route); } }
        public WeatherMode CurrentMode { get { return weatherRoute && weatherRoute == Route ? forcedWeather : WeatherMode.Automatic; } }

        public string SetTime(float hour)
        {
            var clock = Clock(Route);
            if (!clock)
                return "The day/night cycle is not available here.";

            GameTools.Write(clock, "isMorning", false);
            GameTools.Write(clock, "isPaused", false);
            clockSeconds.SetValue(clock, hour * SecondsPerHour);
            RefreshClock(clock);
            return "Time set to " + TimeLabel() + ".";
        }

        public string ToggleFreeze()
        {
            var clock = Clock(Route);
            if (!clock)
                return "The day/night cycle is not available here.";

            if (frozenClock == clock)
            {
                frozenClock = null;
                GameTools.Write(clock, "isMorning", false);
                GameTools.Write(clock, "isPaused", false);
                return "World clock resumed.";
            }

            frozenClock = clock;
            return "World clock frozen at " + TimeLabel() + ".";
        }

        public bool AllowClockUpdate(object instance)
        {
            if (!frozenClock || frozenClock != instance as Component)
                return true;

            var follow = GameTools.Read<Transform>(frozenClock, "attachedTo");
            if (follow)
                frozenClock.transform.position = follow.position;
            return false;
        }

        private void RefreshClock(Component clock)
        {
            float seconds = (float)clockSeconds.GetValue(clock);
            float hour = Mathf.Repeat(seconds / SecondsPerHour, 24f);
            GameTools.Write(clock, "timeInHours", hour);
            clock.transform.rotation = Quaternion.Euler(0f, 0f, hour * 15f);
            bool day = hour >= 6f && hour <= 18f;
            var director = GameTools.Read<GameObject>(clock, "director");
            if (GameTools.Read<bool>(clock, "isIlluminated") == day)
            {
                GameTools.Write(clock, "isIlluminated", !day);
                if (director)
                    director.SendMessage(day ? "IlluminateStop" : "Illuminate", SendMessageOptions.DontRequireReceiver);
            }
            if (GameTools.Read<bool>(clock, "isDay") != day)
            {
                GameTools.Write(clock, "isDay", day);
                if (director)
                    director.SendMessage(day ? "DayGo" : "NightGo", SendMessageOptions.DontRequireReceiver);
            }

            var transitions = GameTools.Read<Array>(clock, "timeOfDayTransitions");
            object latest = null;
            object previous = null;
            float age = 25f;
            float previousAge = 25f;
            if (transitions != null)
            {
                foreach (object transition in transitions)
                {
                    if (transition == null || !(bool)transitionEnabled.GetValue(transition))
                        continue;
                    float distance = Mathf.Repeat(hour - (float)transitionStart.GetValue(transition), 24f);
                    if (distance < age)
                    {
                        previous = latest;
                        previousAge = age;
                        latest = transition;
                        age = distance;
                    }
                    else if (distance < previousAge)
                    {
                        previous = transition;
                        previousAge = distance;
                    }
                }
            }
            if (latest == null)
                return;

            if (previous != null && age < (float)transitionDuration.GetValue(latest))
            {
                var source = environmentFromTransition.Invoke(null, new[] { previous });
                GameTools.Write(clock, "_sourceEnvironmentState", source);
                GameTools.Write(clock, "_currentTransition", latest);
                applyTransition.Invoke(clock, null);
            }
            else
            {
                GameTools.Write(clock, "_currentTransition", null);
                var target = environmentFromTransition.Invoke(null, new[] { latest });
                applyEnvironment.Invoke(clock, new[] { target });
            }
        }

        public string SetWeather(WeatherMode mode)
        {
            var route = Route;
            var clock = Clock(route);
            if (!route || !clock)
                return "Weather controls are not available here.";

            if (mode == WeatherMode.Automatic)
            {
                if (OwnsWeather(route))
                {
                    ApplyWeather(route, clock, naturalWeather);
                    forcedWeather = WeatherMode.Automatic;
                    weatherRoute = null;
                    ClearLightning();
                }
                return "Automatic route weather enabled.";
            }

            WeatherMode baseline = OwnsWeather(route) ? naturalWeather : ReadWeather(route, clock);
            ApplyWeather(route, clock, mode);
            weatherRoute = route;
            naturalWeather = baseline;
            forcedWeather = mode;
            ClearLightning();
            if (mode == WeatherMode.Storm)
                nextFlash = Time.time + 1.5f;
            return mode + " weather enabled.";
        }

        public bool OwnsWeather(object instance)
        {
            return weatherRoute && weatherRoute == instance as Component && forcedWeather != WeatherMode.Automatic;
        }

        public GameObject NativeRainEmitter(object instance)
        {
            return OwnsWeather(instance) ? GameTools.Read<GameObject>(weatherRoute, "rainTarget") : null;
        }

        public void AfterNativeWeather(object instance, GameObject previousRain)
        {
            if (!OwnsWeather(instance))
                return;

            var clock = Clock(weatherRoute);
            if (!clock)
                return;
            var currentRain = GameTools.Read<GameObject>(weatherRoute, "rainTarget");
            if (previousRain && previousRain != currentRain)
                Object.Destroy(previousRain);
            naturalWeather = ReadWeather(weatherRoute, clock);
            ApplyWeather(weatherRoute, clock, forcedWeather);
        }

        private WeatherMode ReadWeather(Component route, Component clock)
        {
            int rain = GameTools.Read<int>(route, "rainType");
            if (rain == 2)
                return WeatherMode.Storm;
            if (rain == 1)
                return WeatherMode.Rain;

            var transitions = GameTools.Read<Array>(clock, "timeOfDayTransitions");
            return transitions != null && transitions.Length > 0 &&
                transitions.GetValue(0) != null && (bool)transitionEnabled.GetValue(transitions.GetValue(0))
                ? WeatherMode.Sunny : WeatherMode.Fog;
        }

        private void ApplyWeather(Component route, Component clock, WeatherMode mode)
        {
            var transitions = GameTools.Read<Array>(clock, "timeOfDayTransitions");
            if (transitions == null || transitions.Length < 12)
                throw new InvalidOperationException("The current location does not have Jalopy's weather profiles.");

            bool rain = mode == WeatherMode.Rain || mode == WeatherMode.Storm;
            var template = rain ? GameTools.Read<GameObject>(route, mode == WeatherMode.Storm ? "heavyRain" : "lightRain") : null;
            if (rain && !template)
                throw new InvalidOperationException("The selected rain prefab is unavailable.");

            var director = route.GetComponent(directorType);
            var car = director ? GameTools.Read<GameObject>(director, "carLogic") : null;
            var carLogic = car ? car.GetComponent(carType) : null;
            var performance = car ? car.GetComponent(performanceType) : null;
            var sound = clock.GetComponent(soundType);
            var seatObject = GameTools.Read<GameObject>(route, "seatLogic");
            var seat = seatObject ? seatObject.GetComponent(seatType) : null;
            var oldRain = GameTools.Read<GameObject>(route, "rainTarget");
            GameObject newRain = null;
            if (template)
            {
                var camera = Camera.main;
                Vector3 position = camera ? camera.transform.position + Vector3.up * 10f : route.transform.position;
                newRain = Object.Instantiate(template, position, Quaternion.identity);
            }
            if (oldRain)
                Object.Destroy(oldRain);
            GameTools.Write(route, "rainTarget", newRain);
            GameTools.Write(route, "rainType", rain ? (mode == WeatherMode.Storm ? 2 : 1) : 0);
            GameTools.Write(route, "currentWeatherCondition", (int)mode);

            // RouteGeneratorC.SetWeather uses these four-entry banks for the weather and lighting.
            int first = mode == WeatherMode.Sunny ? 0 : (mode == WeatherMode.Storm ? 4 : 8);
            for (int i = 0; i < 12; i++)
            {
                var transition = transitions.GetValue(i);
                if (transition != null)
                    transitionEnabled.SetValue(transition, i >= first && i < first + 4);
            }
            if (sound)
            {
                string audio = mode == WeatherMode.Sunny ? "SetSunnyAudio" : mode == WeatherMode.Fog
                    ? "SetFogAudio" : mode == WeatherMode.Rain ? "SetLightRainAudio" : "SetHeavyRainAudio";
                GameTools.Call(sound, audio);
            }
            if (carLogic)
            {
                string windows = mode == WeatherMode.Rain ? "SetLightRainWindows" :
                    mode == WeatherMode.Storm ? "SetHeavyRainWindows" : "SetDryWindows";
                GameTools.Call(carLogic, windows);
            }
            if (performance)
                GameTools.Call(performance, "UpdateAllWheelGrip");
            if (rain && seat && GameTools.Read<bool>(seat, "isSat"))
                GameTools.Call(seat, "ForceRainAudio");

            RefreshClock(clock);
        }

        public void TickLightning()
        {
            if (forcedWeather != WeatherMode.Storm)
                return;
            if (!weatherRoute || weatherRoute != Route)
            {
                SetAutomatic();
                return;
            }
            float time = Time.time;
            if (time >= nextFlash)
            {
                if (!lightning)
                {
                    var flash = new GameObject("Jalopy Storm Flash");
                    flash.transform.SetParent(weatherRoute.transform, false);
                    flash.transform.rotation = Quaternion.Euler(36f, 120f, 0f);
                    lightning = flash.AddComponent<Light>();
                    lightning.type = LightType.Directional;
                    lightning.shadows = LightShadows.None;
                    lightning.color = new Color(0.72f, 0.82f, 1f);
                }
                flashStart = time;
                nextFlash = time + 12f + (float)lightningRandom.NextDouble() * 18f;
            }
            if (lightning)
            {
                float elapsed = time - flashStart;
                lightning.enabled = elapsed < 0.09f || (elapsed >= 0.16f && elapsed < 0.23f);
                lightning.intensity = elapsed < 0.09f ? 1.35f : 0.65f;
            }
        }

        public void Release()
        {
            frozenClock = null;
            if (weatherRoute && weatherRoute == Route)
                SetWeather(WeatherMode.Automatic);
            SetAutomatic();
        }

        private void SetAutomatic()
        {
            forcedWeather = WeatherMode.Automatic;
            weatherRoute = null;
            ClearLightning();
        }

        private void ClearLightning()
        {
            if (lightning)
                Object.Destroy(lightning.gameObject);
            lightning = null;
            flashStart = float.NegativeInfinity;
        }
    }
}

using UnityEngine;
using UnityEngine.Rendering;

namespace Workerborn
{
    public sealed class VillageLighting : MonoBehaviour
    {
        [SerializeField] private Light sun;
        [SerializeField] private Light[] lanterns;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private VillageWeather weather;
        [SerializeField, Range(0f, 24f)] private float dawnHour = 5.5f;
        [SerializeField, Range(0f, 24f)] private float duskHour = 17.5f;
        [SerializeField, Min(.1f)] private float transitionHours = 2f;
        [SerializeField] private Color dayLight = new Color(1f, .96f, .88f);
        [SerializeField] private Color nightLight = new Color(.68f, .8f, 1f);
        [SerializeField, Range(.1f, 1f)] private float nightIntensity = .77f;
        [SerializeField] private Color nightSkyTint = new Color(.31f, .43f, .58f);
        [SerializeField] private Color dayAmbient = new Color(.79f, .84f, .77f);
        [SerializeField] private Color nightAmbient = new Color(.56f, .68f, .83f);

        private static readonly int Tint = Shader.PropertyToID("_Tint");
        private Material sourceSky, sceneSky;
        private Color daySkyTint;
        private float sunIntensity;
        private float[] lanternIntensities;
        private Vector3 sunAngles;
        private float previousNight = -1f;
        private CameraClearFlags outdoorClearFlags;
        private int outdoorCullingMask;
        private Color outdoorBackground;
        private bool indoors;

        private void Awake()
        {
            if (worldCamera != null)
            {
                outdoorClearFlags = worldCamera.clearFlags;
                outdoorCullingMask = worldCamera.cullingMask;
                outdoorBackground = worldCamera.backgroundColor;
            }
            if (sun != null)
            {
                sunIntensity = sun.intensity;
                sunAngles = sun.transform.eulerAngles;
            }
            lanternIntensities = new float[lanterns?.Length ?? 0];
            for (int i = 0; i < lanternIntensities.Length; i++)
                if (lanterns[i] != null)
                    lanternIntensities[i] = lanterns[i].intensity;

            sourceSky = RenderSettings.skybox;
            if (sourceSky != null && sourceSky.HasProperty(Tint))
            {
                daySkyTint = sourceSky.GetColor(Tint);
                sceneSky = new Material(sourceSky) { name = sourceSky.name + " (Village Lighting)" };
                RenderSettings.skybox = sceneSky;
            }
        }

        public string WeatherName(int day) => weather != null ? weather.NameForDay(day) : "";

        internal void SetInteriorBackground(bool inside)
        {
            if (worldCamera == null || indoors == inside)
                return;
            indoors = inside;
            worldCamera.clearFlags = inside ? CameraClearFlags.SolidColor : outdoorClearFlags;
            worldCamera.backgroundColor = inside ? Color.black : outdoorBackground;
            worldCamera.cullingMask = inside ? outdoorCullingMask & ~(1 << VillageWeather.SkyLayer) : outdoorCullingMask;
        }

        public void SetDate(int day, float minute)
        {
            weather?.SetDate(day, minute);
            SetTime(minute);
        }

        public void SetTime(float minute)
        {
            float hour = Mathf.Repeat(minute / 60f, 24f);
            float daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(dawnHour, dawnHour + transitionHours, hour))
                * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(duskHour, duskHour + transitionHours, hour));
            float night = 1f - daylight;
            if (Mathf.Approximately(night, previousNight))
                return;
            previousNight = night;

            if (sun != null)
            {
                sun.color = Color.Lerp(dayLight, nightLight, night);
                sun.intensity = sunIntensity * Mathf.Lerp(1f, nightIntensity, night);
                sun.transform.rotation = Quaternion.Euler(sunAngles.x, sunAngles.y + night * 28f, sunAngles.z);
            }
            if (sceneSky != null)
                sceneSky.SetColor(Tint, Color.Lerp(daySkyTint, nightSkyTint, night));
            outdoorBackground = Color.Lerp(dayAmbient, nightAmbient, night);
            if (worldCamera != null && !indoors)
                worldCamera.backgroundColor = outdoorBackground;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(dayAmbient, nightAmbient, night);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(.54f, .61f, .49f), new Color(.39f, .5f, .66f), night);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(.36f, .42f, .31f), new Color(.27f, .35f, .48f), night);

            float glow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.35f, .9f, night));
            for (int i = 0; i < lanternIntensities.Length; i++)
                if (lanterns[i] != null)
                {
                    lanterns[i].intensity = lanternIntensities[i] * glow;
                    lanterns[i].enabled = glow > .001f;
                }
        }

        private void OnDestroy()
        {
            if (sceneSky == null)
                return;
            if (RenderSettings.skybox == sceneSky)
                RenderSettings.skybox = sourceSky;
            Destroy(sceneSky);
        }
    }
}

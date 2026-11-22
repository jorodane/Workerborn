using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class SettingsWindowView : VillageWindowView
    {
        private const string VolumeKey = "Workerborn.Settings.MasterVolume";
        private const string FullscreenKey = "Workerborn.Settings.Fullscreen";
        [SerializeField] private Slider volume;
        [SerializeField] private TMP_Text volumeValue;
        [SerializeField] private Button screenMode;
        [SerializeField] private TMP_Text screenModeValue;
        [SerializeField] private Button apply;
        private bool fullscreen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RestoreSettings()
        {
            if (Application.isBatchMode || VillageSession.BootDedicated)
                return;
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, AudioListener.volume));
            if (PlayerPrefs.HasKey(FullscreenKey))
                Screen.fullScreen = PlayerPrefs.GetInt(FullscreenKey) != 0;
        }

        public override void Bind(VillagePrototype game, VillageHud hud)
        {
            base.Bind(game, hud);
            volume.onValueChanged.AddListener(_ => Refresh());
            screenMode.onClick.AddListener(() =>
            {
                fullscreen = !fullscreen;
                Refresh();
            });
            apply.onClick.AddListener(Apply);
            ReadSettings();
        }

        private void OnEnable()
        {
            if (Game != null)
                ReadSettings();
        }

        private void ReadSettings()
        {
            fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) != 0;
            volume.value = AudioListener.volume;
            Refresh();
        }

        public override void Refresh()
        {
            volumeValue.text = $"{Mathf.RoundToInt(volume.value * 100f)}%";
            screenModeValue.text = fullscreen ? "전체 화면" : "창 모드";
        }

        private void Apply()
        {
            AudioListener.volume = volume.value;
            Screen.fullScreen = fullscreen;
            PlayerPrefs.SetFloat(VolumeKey, volume.value);
            PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
            PlayerPrefs.Save();
            Hud.ShowToast("환경설정을 적용했어요.");
        }
    }
}

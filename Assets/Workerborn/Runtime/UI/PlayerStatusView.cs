using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class PlayerStatusView : MonoBehaviour
    {
        [SerializeField] private TMP_Text characterName;
        [SerializeField] private TMP_Text level;
        [SerializeField] private HealthBarView healthBar;
        [SerializeField] private TMP_Text health;
        [SerializeField] private Image staminaFill;
        [SerializeField] private TMP_Text stamina;
        [SerializeField] private Image fullnessFill;
        [SerializeField] private TMP_Text fullness;
        [SerializeField] private TMP_Text gold;
        [SerializeField] private TMP_Text ap;
        [SerializeField] private TMP_Text attributes;
        private VillageState shownState;
        private int shownVersion = -1;
        private int shownInventoryVersion = -1;

        internal RectTransform Rect => (RectTransform)transform;

        internal void Refresh(string name, VillageState state)
        {
            characterName.text = name;
            healthBar.SetValue(state.Health, state.HealthLimit);
            health.SetText("{0:0} / {1:0}", state.Health, state.HealthLimit);
            staminaFill.fillAmount = state.StaminaFill;
            stamina.SetText("{0:0} / {1:0}", state.Energy, state.StaminaLimit);
            fullnessFill.fillAmount = state.FullnessFill;
            fullnessFill.color = state.Overeating > 0f ? new Color(1f, .52f, .32f) : new Color(.87f, .70f, .37f);
            fullness.SetText("{0:0} / {1:0}", state.Fullness, state.FullnessLimit);

            if (shownState == state && shownVersion == state.Version && shownInventoryVersion == state.Inventory.Version)
                return;
            shownState = state;
            shownVersion = state.Version;
            shownInventoryVersion = state.Inventory.Version;
            level.SetText("Lv. {0:0}", state.Level);
            gold.text = $"{state.Gold:N0} Gold";
            ap.text = $"AP {state.AP:N0}";
            attributes.text = $"힘 {state.Strength}   솜씨 {state.Dexterity}   지능 {state.Knowledge}\n" +
                $"근성 {state.Grit}   소화 {state.Digestion}";
        }
    }
}

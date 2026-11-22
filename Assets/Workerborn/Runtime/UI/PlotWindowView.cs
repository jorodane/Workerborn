using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class PlotWindowView : VillageWindowView
    {
        [SerializeField] private TMP_Text heading;
        [SerializeField] private TMP_Text status;
        [SerializeField] private RectTransform choices;
        [SerializeField] private ScrollRect scroll;

        private VillagePlot plot;
        private string shownFacility;
        private bool populated;

        internal void Show(VillagePlot target)
        {
            plot = target;
            populated = false;
            Refresh();
        }

        public override void Refresh()
        {
            if (plot == null)
                return;

            var state = Game.State;
            string installed = state.FacilityAt(plot.Id);
            if (populated && shownFacility == installed)
                return;
            populated = true;
            shownFacility = installed;

            PlotFootprint available = plot.Footprint;
            string type = available.Ground == PlotGround.Fishing ? "낚시 전용" : "일반";
            heading.text = $"{plot.DisplayName}  ·  {available.Width}×{available.Depth}칸  ·  {type}";
            var current = Game.Settings.FindPlotFacility(installed);
            status.text = installed == null ? "비어 있음" : current != null && current.FillsPlot
                ? $"{current.Title} 설치됨"
                : $"{current?.Title ?? installed} 설치됨 · 변경 불가";

            InventoryWindowView.Clear(choices);
            var catalog = Game.Settings.PlotFacilities;
            for (int i = 0; i < catalog.Count; i++)
            {
                int choice = i;
                if (!state.CanInstallFacility(plot.Id, choice, out _))
                    continue;
                var facility = catalog[choice];
                PlotFootprint size = facility.Footprint;
                string detail = facility.FillsPlot ? $"최소 {size.Width}×{size.Depth}칸 · 부지 전체 사용" : $"{size.Width}×{size.Depth}칸 · 중앙에 설치";
                var row = Instantiate(Hud.Library.ListRow, choices);
                row.Bind(facility.Title, detail, null, 0f, () => Install(choice));
            }
            scroll.verticalNormalizedPosition = 1f;
        }

        private void Install(int choice)
        {
            if (Game.BuildFacility(plot, choice))
                Hud.CloseWindow(Id);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Workerborn.Tests
{
    public sealed class InteractionPresentationTests
    {
        [Test]
        public void PlainNamesKeepTheirOriginalPositionsIncludingViewportEdges()
        {
            var labels = new List<NameplateSlot>
            {
                new NameplateSlot
                {
                    Order = 2,
                    X = -20,
                    Y = -5,
                    Width = 80,
                    Height = 24
                },
                new NameplateSlot
                {
                    Order = 1,
                    X = -20,
                    Y = -5,
                    Width = 80,
                    Height = 24
                }
            };
            NameplateLayout.Present(labels, false, 0, 100, 3);
            Assert.That(labels.TrueForAll(l => l.PlacedY == -5 && l.X == -20), Is.True);
            NameplateLayout.Present(labels, true, 0, 100, 3);
            Assert.That(labels[0].Order, Is.EqualTo(1));
            Assert.That(labels[0].PlacedY, Is.GreaterThan(labels[1].PlacedY));
            NameplateLayout.Present(labels, false, 0, 100, 3);
            Assert.That(labels.TrueForAll(l => l.PlacedY == -5 && l.X == -20), Is.True);
        }

        [Test]
        public void EqualScreenHeightsStackInCreationOrderAndKeepTheirHorizontalPositions()
        {
            var slots = new List<NameplateSlot>();
            foreach (int order in new[]
            {
                4,
                2,
                5,
                1,
                3
            }

            )
                slots.Add(new NameplateSlot { Order = order, X = 100, Y = 150, Width = 80, Height = 24 });
            NameplateLayout.Arrange(slots, 0, 300, 3);
            Assert.That(slots.Select(s => s.Order), Is.EqualTo(new long[] { 1, 2, 3, 4, 5 }));
            for (int i = 0; i < slots.Count; i++)
            {
                Assert.That(slots[i].X, Is.EqualTo(100));
                Assert.That(slots[i].PlacedY, Is.InRange(12, 288));
                if (i > 0)
                    Assert.That(slots[i - 1].PlacedY - slots[i].PlacedY, Is.GreaterThanOrEqualTo(27));
            }
        }

        [Test]
        public void ScreenHeightTakesPriorityOverCreationOrderAndSeparateColumnsStayStill()
        {
            var slots = new List<NameplateSlot>
            {
                new NameplateSlot
                {
                    Order = 1,
                    X = 50,
                    Y = 100,
                    Width = 70,
                    Height = 24
                },
                new NameplateSlot
                {
                    Order = 2,
                    X = 50,
                    Y = 120,
                    Width = 70,
                    Height = 24
                },
                new NameplateSlot
                {
                    Order = 3,
                    X = 250,
                    Y = 120,
                    Width = 70,
                    Height = 24
                }
            };
            NameplateLayout.Arrange(slots, 0, 300, 3);
            Assert.That(slots.Find(s => s.Order == 2).PlacedY, Is.GreaterThan(slots.Find(s => s.Order == 1).PlacedY));
            Assert.That(slots.Find(s => s.Order == 3).PlacedY, Is.EqualTo(120));
        }

        [Test]
        public void OverlapUsesObjectHeightAndLeavesDistantLabelsInTheSameColumnAlone()
        {
            var crop = new NameplateSlot { Order = 2, X = 50, Y = 100, ObjectY = 90, Width = 60, Height = 24 };
            var npc = new NameplateSlot { Order = 1, X = 50, Y = 114, ObjectY = 70, Width = 60, Height = 24 };
            var distant = new NameplateSlot { Order = 3, X = 50, Y = 206, ObjectY = 200, Width = 60, Height = 24 };
            var slots = new List<NameplateSlot> { npc, distant, crop };
            NameplateLayout.Arrange(slots, 0, 300, 3);
            Assert.That(crop.PlacedY - npc.PlacedY, Is.GreaterThanOrEqualTo(27));
            Assert.That(distant.PlacedY, Is.EqualTo(distant.Y));
        }

        [Test]
        public void ExpansionOnlyMovesAnotherStackWhenTheirBoxesMeet()
        {
            var a = new NameplateSlot { Order = 1, X = 50, Y = 90, Width = 60, Height = 20 };
            var b = new NameplateSlot { Order = 2, X = 50, Y = 90, Width = 60, Height = 20 };
            var c = new NameplateSlot { Order = 3, X = 50, Y = 55, Width = 60, Height = 20 };
            var slots = new List<NameplateSlot> { a, b, c };
            NameplateLayout.Arrange(slots, 0, 180, 5);
            Assert.That(a.PlacedY - b.PlacedY, Is.GreaterThanOrEqualTo(25));
            Assert.That(b.PlacedY - c.PlacedY, Is.GreaterThanOrEqualTo(25));
        }

        [Test]
        public void RemovingACollectedItemClosesTheGapAndEdgeStacksStayInsideTheScreen()
        {
            var slots = new List<NameplateSlot>
            {
                new NameplateSlot
                {
                    Order = 1,
                    X = 50,
                    Y = 2,
                    Width = 60,
                    Height = 24
                },
                new NameplateSlot
                {
                    Order = 2,
                    X = 50,
                    Y = 2,
                    Width = 60,
                    Height = 24
                }
            };
            NameplateLayout.Arrange(slots, 0, 100, 4);
            Assert.That(slots.All(s => s.PlacedY >= 12 && s.PlacedY <= 88), Is.True);
            slots.RemoveAt(0);
            NameplateLayout.Arrange(slots, 0, 100, 4);
            Assert.That(slots[0].PlacedY, Is.EqualTo(12));
        }

        [Test]
        public void OversizedStacksExposeAStableVerticalScrollRange()
        {
            var slots = Enumerable.Range(0, 10).Select(i => new NameplateSlot { Order = i, X = 50, Y = 50, Width = 80, Height = 24 }).ToList();
            float overflow = NameplateLayout.Arrange(slots, 0, 100, 4);
            Assert.That(overflow, Is.GreaterThan(0));
            float first = slots[0].PlacedY;
            NameplateLayout.Arrange(slots, 0, 100, 4, overflow);
            Assert.That(slots[0].PlacedY - first, Is.EqualTo(overflow).Within(.01f));
            Assert.That(slots[slots.Count - 1].PlacedY, Is.EqualTo(12).Within(.01f));
        }

        [Test]
        public void TargetLineEndsOnAConcaveSilhouetteRatherThanItsBoundingBox()
        {
            var mask = new byte[6 * 6 * 4];
            // L-shaped target; the cursor sits in the missing upper-right corner of its bounds.
            for (int y = 1; y <= 4; y++)
                Paint(mask, 6, 1, y, 1);
            for (int x = 1; x <= 4; x++)
                Paint(mask, 6, x, 1, 1);
            Assert.That(TargetMaskGeometry.Closest(mask, 6, 6, false, 45, 35, 60, 60, out var hit), Is.True);
            Assert.That(hit.Id, Is.EqualTo(1));
            Assert.That(hit.X, Is.EqualTo(45));
            Assert.That(hit.Y, Is.EqualTo(20));
        }

        [Test]
        public void TargetUnderThePointerWinsEvenWhenABoundaryTieFavorsAnotherId()
        {
            var mask = new byte[8 * 8 * 4];
            for (int y = 1; y < 7; y++)
                for (int x = 1; x < 6; x++)
                    Paint(mask, 8, x, y, 9);
            Paint(mask, 8, 6, 3, 1);
            Assert.That(TargetMaskGeometry.Closest(mask, 8, 8, false, 35, 35, 80, 80, out var hit), Is.True);
            Assert.That(hit.Id, Is.EqualTo(9));
            Assert.That(hit.DistanceSquared, Is.EqualTo(625));
        }

        [Test]
        public void EmptyOrInvalidMasksDoNotLeaveAStaleSelection()
        {
            Assert.That(TargetMaskGeometry.Closest(new byte[16], 2, 2, false, 1, 1, 100, 100, out _), Is.False);
            Assert.That(TargetMaskGeometry.Closest(new byte[4], 2, 2, false, 1, 1, 100, 100, out _), Is.False);
        }

        [Test]
        public void DirectPickingUsesOnlyPaintedPixelsAndCtrlCanSkipProps()
        {
            var mask = new byte[8 * 8 * 4];
            Paint(mask, 8, 2, 2, 1);
            Paint(mask, 8, 6, 2, 8);
            Assert.That(TargetMaskGeometry.IdAt(mask, 8, 8, 2, 2, false), Is.EqualTo(1));
            Assert.That(TargetMaskGeometry.IdAt(mask, 8, 8, 3, 2, false), Is.Zero);
            Assert.That(TargetMaskGeometry.Closest(mask, 8, 8, false, 25, 25, 80, 80, out var hit, id => id == 8), Is.True);
            Assert.That(hit.Id, Is.EqualTo(8));
        }

        [Test]
        public void PointerCoordinatesUseThePublishedMaskOrientation()
        {
            var mask = new byte[4 * 3 * 4];
            Paint(mask, 4, 1, 0, 3);
            Paint(mask, 4, 1, 2, 7);
            Assert.That(TargetMaskGeometry.IdAtScreen(mask, 4, 3, true, 150, 20, 400, 300), Is.EqualTo(7));
            Assert.That(TargetMaskGeometry.IdAtScreen(mask, 4, 3, false, 150, 20, 400, 300), Is.EqualTo(3));
            Assert.That(TargetMaskGeometry.IdAtScreen(mask, 4, 3, true, -1, 20, 400, 300), Is.Zero);
        }

        [Test]
        public void SelectionOutlineLeavesEnclosedHolesUnmarked()
        {
            const int width = 9;
            var mask = new byte[width * width * 4];
            for (int y = 2; y <= 6; y++)
                for (int x = 2; x <= 6; x++)
                    if (x == 2 || x == 6 || y == 2 || y == 6)
                        Paint(mask, width, x, y, 3);
            var exterior = new byte[width * width];
            var queue = new int[exterior.Length];
            TargetMaskGeometry.MarkExterior(mask, width, width, 3, exterior, queue);
            Assert.That(exterior[4 * width + 4], Is.Zero);
            Assert.That(exterior[4 * width + 2], Is.EqualTo(255));
            Assert.That(exterior[4 * width + 1], Is.EqualTo(128));
            var rgba = new byte[mask.Length];
            Assert.That(TargetMaskGeometry.DrawExteriorOutline(mask, width, width, 3, 1, exterior, queue, rgba), Is.True);
            Assert.That(rgba[(4 * width + 4) * 4 + 3], Is.Zero, "Interior holes stay transparent.");
            Assert.That(rgba[(4 * width + 2) * 4 + 3], Is.Zero, "The selected surface stays transparent.");
            Assert.That(rgba[(4 * width + 1) * 4 + 3], Is.EqualTo(255), "The outside edge is drawn.");
        }

        [Test]
        public void ReadbackOrientationAndViewportScalingProduceTheSameEdge()
        {
            var bottom = new byte[4 * 4 * 4];
            Paint(bottom, 4, 1, 0, 3);
            var top = new byte[bottom.Length];
            Array.Copy(bottom, 0, top, 3 * 4 * 4, 4 * 4);
            TargetMaskGeometry.Closest(bottom, 4, 4, false, 10, 40, 40, 40, out var a);
            TargetMaskGeometry.Closest(top, 4, 4, true, 10, 40, 40, 40, out var b);
            Assert.That(a.Id, Is.EqualTo(b.Id));
            Assert.That(a.X, Is.EqualTo(b.X));
            Assert.That(a.Y, Is.EqualTo(b.Y));
        }

        [Test]
        public void MultiItemPurchasesAndSalesAreAtomicWithPhysicalCoins()
        {
            var state = VillageState.Restore(new VillageSave { gold = 50 });
            Assert.That(state.TryBuy(ItemIds.Bread, 4), Is.True);
            Assert.That(state.Gold, Is.EqualTo(10));
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.EqualTo(4));
            Assert.That(state.TryBuy(ItemIds.Bread, 2), Is.False);
            Assert.That(state.Gold, Is.EqualTo(10));
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.EqualTo(4));
            Assert.That(state.TrySellItems(ItemIds.Bread, 3), Is.True);
            Assert.That(state.Gold, Is.EqualTo(40));
            Assert.That(state.Inventory.Count(ItemIds.Bread), Is.EqualTo(1));
        }

        private static void Paint(byte[] bytes, int width, int x, int y, int id)
        {
            int index = (y * width + x) * 4;
            bytes[index] = (byte)id;
            bytes[index + 3] = 255;
        }
    }
}

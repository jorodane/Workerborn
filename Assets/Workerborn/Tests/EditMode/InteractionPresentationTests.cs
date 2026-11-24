using System;
using NUnit.Framework;

namespace Workerborn.Tests
{
    public sealed class InteractionPresentationTests
    {
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

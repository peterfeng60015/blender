namespace Sidescroller
{
    /// <summary>排序（只用 Default sorting layer，只靠 sortingOrder）。</summary>
    public static class SsSorting
    {
        public const int BgFar = -1000, BgMid = -900, Landmark = -600, Props = -500, Terrain = -100;
        public const int Npc = 0, Enemy = 10, Boss = 15, Hero = 20, Projectile = 30, Fx = 40;
    }
}

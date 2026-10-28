using UnityEngine;

/// <summary>
/// 遊戲模組代碼（Assembly-CSharp）的入口。Entry 場景的 EntrySceneController 載完 dll 後以反射呼叫
/// <see cref="Run"/>，所以型別名稱、無命名空間、public static 無參數這幾點都不能改
/// （改了要同步改 EntrySceneController 的 GameEntryTypeName / GameEntryMethodName）。
/// </summary>
public static class AppBootstrap
{
    public static void Run()
    {
        Debug.Log($"[AppBootstrap] 遊戲模組代碼已執行（Assembly: {typeof(AppBootstrap).Assembly.GetName().Name}，" +
                  $"App 版本: {Application.version}）");
        Glowshaft.GlowshaftGame.Boot();
    }
}

using UnityEngine;

/// <summary>
/// 熱更代碼（Assembly-CSharp）的入口。Entry 場景的 EntrySceneController 載完 dll 後以反射呼叫
/// <see cref="Run"/>，所以型別名稱、無命名空間、public static 無參數這幾點都不能改
/// （改了要同步改 EntrySceneController 的 HotUpdateEntryTypeName / HotUpdateEntryMethodName）。
/// </summary>
public static class HotUpdateMain
{
    public static void Run()
    {
        Debug.Log($"[HotUpdateMain] 熱更代碼已執行（Assembly: {typeof(HotUpdateMain).Assembly.GetName().Name}，" +
                  $"App 版本: {Application.version}）");
    }
}

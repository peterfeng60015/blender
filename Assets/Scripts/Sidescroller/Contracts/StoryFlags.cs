using System;
using System.Collections.Generic;

namespace Sidescroller
{
    /// <summary>劇情旗標。內部用 List&lt;string&gt; 存值為 true 的旗標；tmp_ 開頭的在重生時清掉。</summary>
    public sealed class StoryFlags
    {
        public const string TempPrefix = "tmp_";

        private readonly List<string> _on = new List<string>();

        /// <summary>(flag, value)，值真的變了才觸發。</summary>
        public event Action<string, bool> Changed;

        /// <summary>沒設過＝false。</summary>
        public bool Get(string flag) => !string.IsNullOrEmpty(flag) && _on.Contains(flag);

        public void Set(string flag, bool value = true)
        {
            if (string.IsNullOrEmpty(flag) || Get(flag) == value)
                return;
            if (value)
                _on.Add(flag);
            else
                _on.Remove(flag);
            Changed?.Invoke(flag, value);
        }

        /// <summary>把 tmp_ 開頭的旗標全設回 false（各觸發一次 Changed）。</summary>
        public void ClearTemporary()
        {
            var temp = _on.FindAll(f => f.StartsWith(TempPrefix, StringComparison.Ordinal));
            foreach (var flag in temp)
                Set(flag, false);
        }
    }
}

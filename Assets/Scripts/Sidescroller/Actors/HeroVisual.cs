using UnityEngine;

namespace Sidescroller
{
    /// <summary>
    /// 主角的外觀與動畫。<see cref="SsArt.HasHero3D"/> 時用 3D 模型（Animator 播 SsHero3D_Anim 的狀態），
    /// 否則照舊用 <see cref="SpriteAnimator"/> 播 sprite sheet。HeroController 只透過這裡改外觀，不必知道是哪一種。
    ///
    /// 3D 模型：
    ///   - 匯入後面朝 -Z（朝相機）；面向右轉成 yaw -<see cref="TurnYaw"/>、面向左 +<see cref="TurnYaw"/>，
    ///     比正側面多轉向鏡頭一點，臉和郵差包都看得到。
    ///   - shader 不吃場景燈（Sidescroller/AhJiaToon），受擊閃色用 MaterialPropertyBlock 改 _Tint；
    ///     無敵閃爍（sprite 是半透明）改成整隻隱藏／顯示。
    ///   - Animator 用 scaled time（暫停時停住）；不循環的動作播完由 Animator 停在最後一格，播完與否用自己的計時判斷
    ///     （Animator.Play 下一次更新才生效，當下查 state 會拿到上一個動作）。
    ///   - 目前只有日班制服的模型：特急制服也用同一隻（換裝時照樣閃一下）。
    /// </summary>
    public sealed class HeroVisual : MonoBehaviour
    {
        /// <summary>模型高約 1.1，放大到和 sprite 主角差不多高（1.5 單位）。</summary>
        private const float ModelScale = 1.3f;
        private const float TurnYaw = 70f;
        private const float CrossFade = 0.06f;
        private static readonly int TintId = Shader.PropertyToID("_Tint");

        private SpriteAnimator _sprites;
        private CostumeId _costume;

        private Transform _model;
        private Animator _animator;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private string[] _clipNames;
        private float[] _clipLengths;

        private string _action;
        private bool _loop;
        private float _time;
        private float _flashLeft;
        private bool _visible = true;

        /// <summary>+1 右、-1 左。</summary>
        public int Facing { get; private set; } = 1;
        public bool Is3D => _model != null;
        /// <summary>不循環的動作已播完（停在最後一格）。</summary>
        public bool IsDone => _action != null && !_loop && _time >= Duration(_action);

        public static HeroVisual Create(GameObject root, int sortingOrder, CostumeId costume)
        {
            var visual = root.AddComponent<HeroVisual>();
            visual._costume = costume;
            if (SsArt.HasHero3D)
                visual.Build3D(root.transform, sortingOrder);
            else
                visual._sprites = SpriteAnimator.Create(root, sortingOrder);
            return visual;
        }

        #region 公開 API（和 SpriteAnimator 對齊）
        /// <summary>播放 Idle／Run／Jump／Melee／Shoot／Hurt；同一個動作而且 restart=false 時不重來。</summary>
        public void Play(string action, bool loop, bool restart)
        {
            if (!restart && action == _action && loop == _loop)
                return;
            var same = action == _action;
            _action = action;
            _loop = loop;
            _time = 0f;
            if (_sprites != null)
            {
                _sprites.Play(SsArt.Get(SpriteLocation(action)), loop, true);
                return;
            }
            if (same)
                _animator.Play(action, 0, 0f);
            else
                _animator.CrossFadeInFixedTime(action, CrossFade, 0, 0f);
        }

        /// <summary>換裝：sprite 換一套 sheet 但保留播放時間；3D 目前只有一套模型。</summary>
        public void SetCostume(CostumeId costume)
        {
            _costume = costume;
            if (_sprites != null && _action != null)
                _sprites.Swap(SsArt.Get(SpriteLocation(_action)));
        }

        public void SetFacing(int dir)
        {
            if (dir == 0)
                return;
            Facing = dir > 0 ? 1 : -1;
            if (_sprites != null)
                _sprites.SetFacing(Facing);
            else
                _model.localRotation = Quaternion.Euler(0f, -TurnYaw * Facing, 0f);
        }

        /// <summary>平常的顏色；3D 只看 alpha：小於 1（無敵閃爍）時整隻隱藏。</summary>
        public void SetBaseColor(Color color)
        {
            if (_sprites != null)
            {
                _sprites.SetBaseColor(color);
                return;
            }
            var visible = color.a >= 0.99f;
            if (visible == _visible)
                return;
            _visible = visible;
            foreach (var r in _renderers)
                r.enabled = visible;
        }

        /// <summary>受擊閃色 time 秒（顏色是乘上去的）。</summary>
        public void Flash(Color color, float time)
        {
            if (_sprites != null)
            {
                _sprites.Flash(color, time);
                return;
            }
            _flashLeft = time;
            ApplyTint(color);
        }

        /// <summary>動作的長度（秒）；不認得的動作回 0。</summary>
        public float Duration(string action)
        {
            if (_sprites != null)
                return SsArt.Get(SpriteLocation(action)).Duration;
            var i = System.Array.IndexOf(_clipNames, action);
            return i >= 0 ? _clipLengths[i] : 0f;
        }
        #endregion

        #region 3D
        private void Build3D(Transform root, int sortingOrder)
        {
            var go = Instantiate(SsArt.HeroModel, root, false);
            go.name = "Model";
            _model = go.transform;
            _model.localPosition = Vector3.zero;
            _model.localScale = Vector3.one * ModelScale;
            _model.localRotation = Quaternion.Euler(0f, -TurnYaw * Facing, 0f);

            _animator = go.GetComponent(typeof(Animator)) as Animator;
            if (_animator == null)
                _animator = go.AddComponent(typeof(Animator)) as Animator;
            _animator.runtimeAnimatorController = SsArt.HeroAnimController;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.updateMode = AnimatorUpdateMode.Normal;

            var clips = SsArt.HeroAnimController.animationClips;
            _clipNames = new string[clips.Length];
            _clipLengths = new float[clips.Length];
            for (var i = 0; i < clips.Length; i++)
            {
                _clipNames[i] = clips[i].name;
                _clipLengths[i] = clips[i].length;
            }

            var found = go.GetComponentsInChildren(typeof(Renderer), true);
            _renderers = new Renderer[found.Length];
            for (var i = 0; i < found.Length; i++)
            {
                _renderers[i] = (Renderer)found[i];
                _renderers[i].sortingOrder = sortingOrder;
            }
            _block = new MaterialPropertyBlock();
        }

        private void ApplyTint(Color color)
        {
            foreach (var r in _renderers)
            {
                r.GetPropertyBlock(_block);
                _block.SetColor(TintId, color);
                r.SetPropertyBlock(_block);
            }
        }

        private void Update()
        {
            _time += Time.deltaTime;
            if (_flashLeft > 0f)
            {
                _flashLeft -= Time.deltaTime;
                if (_flashLeft <= 0f)
                    ApplyTint(Color.white);
            }
        }
        #endregion

        private string SpriteLocation(string action) => HeroStats.SpritePrefix(_costume) + "_" + action;
    }
}

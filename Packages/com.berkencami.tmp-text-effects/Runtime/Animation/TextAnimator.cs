using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;

namespace TMPTextEffects
{
    public enum StaggerOrder
    {
        LeftToRight,
        RightToLeft,
        CenterOut,
        EdgesIn,
        Random,
    }

    public enum LoopMode
    {
        Once,
        Loop,
        PingPong,
    }

    /// <summary>An effect applied only to characters inside <c>&lt;Tag&gt;…&lt;/Tag&gt;</c> in the text.</summary>
    [Serializable]
    public class TagEffect
    {
        public string Tag = "wave";
        [SerializeReference] public TextEffect Effect;
    }

    /// <summary>
    /// Per-character text animation for any TMP text, UI or world-space (with or without <see cref="LayeredText"/>).
    /// </summary>
    /// <remarks>
    /// Transition effects (Pop, Fade, Slide, Rotate, Flash) follow <see cref="Progress"/>: 0 = hidden, 1 = at rest,
    /// each character starting at its own time along the text. Looping effects (Wave, Shake, Pulse, Rainbow, Shine)
    /// run on time.
    /// <para>
    /// Inline tags scope effects to parts of the text: <c>&lt;wave&gt;</c>, <c>&lt;shake&gt;</c>, … (see
    /// <see cref="TagEffects"/>), and <c>&lt;pause=0.5&gt;</c> holds the reveal for half a second at that point.
    /// Tags are read through TMP's text preprocessor, so they apply to text set via <c>text</c>, not
    /// <c>SetText(StringBuilder)</c> / <c>SetCharArray</c>.
    /// </para>
    /// <para>
    /// The animator plays itself (<see cref="Play"/>, <see cref="Hide"/>, play-on-enable, loops) — or leave
    /// play-on-enable off and drive <see cref="Progress"/> from anything: an Animator/Timeline clip (it is a
    /// serialized field) or a tween library, e.g.
    /// <c>DOTween.To(() => a.Progress, x => a.Progress = x, 1, a.Duration)</c> or
    /// <c>Tween.Custom(0f, 1f, a.Duration, x => a.Progress = x)</c> (PrimeTween).
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("UI/Text Animator")]
    public class TextAnimator : MonoBehaviour, ITextPreprocessor
    {
        private const int MaxTags = 32; // tag membership is a per-character bit mask
        private const string PauseTag = "pause";

        [SerializeReference] private List<TextEffect> _Effects = new();

        [Tooltip("Effects scoped by inline tags: <Tag>text</Tag>.")]
        [SerializeField] private List<TagEffect> _TagEffects = DefaultTagEffects();

        [Tooltip("Transition position: 0 = hidden, 1 = fully shown. Animatable from Animator/Timeline or tweens.")]
        [SerializeField, Range(0f, 1f)] private float _Progress = 1f;

        [SerializeField] private bool _PlayOnEnable = true;
        [SerializeField, Min(0f)] private float _Delay;
        [Tooltip("Seconds each character takes to animate in.")]
        [SerializeField, Min(0.01f)] private float _CharDuration = 0.35f;
        [Tooltip("Seconds between consecutive characters starting.")]
        [SerializeField, Min(0f)] private float _Stagger = 0.05f;
        [SerializeField] private StaggerOrder _Order = StaggerOrder.LeftToRight;
        [Tooltip("Extra wait after . ! ? … (reading-order staggers only).")]
        [SerializeField, Min(0f)] private float _SentencePause;
        [Tooltip("Extra wait after , ; : (reading-order staggers only).")]
        [SerializeField, Min(0f)] private float _CommaPause;
        [SerializeField] private LoopMode _Loop = LoopMode.Once;
        [Tooltip("Pause at each end before looping.")]
        [SerializeField, Min(0f)] private float _LoopDelay = 1f;
        [SerializeField] private bool _UnscaledTime = true;

        private TMP_Text _tmp;
        private LayeredText _layered;

        private bool _playing;
        private float _direction = 1;
        private float _delayLeft;
        private float _holdLeft;
        private float _effectTime;
        private float _appliedProgress = -1;
        private bool _dirty = true;
        private bool _previewing;

        // Per character (TMP characterInfo index).
        private float[] _startTime = Array.Empty<float>();   // seconds into the transition this character starts
        private int[] _textIndex = Array.Empty<int>();       // reading-order slot among visible characters
        private int[] _revealRank = Array.Empty<int>();      // position in reveal order
        private int[] _tagMask = Array.Empty<int>();
        private int _visibleCount;
        private float _duration;
        private bool _textHasTags;

        // Reveal events: visible characters sorted by start time, and how many have been announced.
        private int[] _revealOrder = Array.Empty<int>();
        private float[] _revealKeys = Array.Empty<float>();
        private int _revealCursor;

        // Preprocessor output, per position of the preprocessed string (= TMP_CharacterInfo.index).
        private readonly StringBuilder _preprocessed = new();
        private int[] _tagMaskBySource = Array.Empty<int>();
        private float[] _pauseBySource = Array.Empty<float>();
        private int _sourceLength;

        private int[] _shuffle = Array.Empty<int>();

        // Plain-TMP mode: TMP's unanimated vertices/colors per mesh, captured on each regeneration.
        private Vector3[][] _srcVerts = Array.Empty<Vector3[]>();
        private Color32[][] _srcColors = Array.Empty<Color32[]>();
        private bool _plainModified;

        /// <summary>Raised when a transition reaches its end (not raised by PingPong loops).</summary>
        public event Action Completed;

        /// <summary>
        /// Raised once per visible character as it starts appearing while playing forward (typewriter sounds,
        /// portraits…). Arguments: TMP character index, the character.
        /// </summary>
        public event Action<int, char> CharacterRevealed;

        public List<TextEffect> Effects => _Effects;
        public List<TagEffect> TagEffects => _TagEffects;

        public float Progress
        {
            get => _Progress;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(value, _Progress)) return;
                _Progress = value;
                _dirty = true;
            }
        }

        /// <summary>Seconds a full 0 → 1 transition takes at the current text (stagger, pauses included).</summary>
        public float Duration => Mathf.Max(_duration, _CharDuration);

        public bool IsPlaying => _playing;

        public float CharDuration { get => _CharDuration; set { _CharDuration = Mathf.Max(0.01f, value); RebuildOrder(); } }
        public float Stagger { get => _Stagger; set { _Stagger = Mathf.Max(0f, value); RebuildOrder(); } }
        public float Delay { get => _Delay; set => _Delay = Mathf.Max(0f, value); }
        public StaggerOrder Order { get => _Order; set { _Order = value; RebuildOrder(); } }
        public float SentencePause { get => _SentencePause; set { _SentencePause = Mathf.Max(0f, value); RebuildOrder(); } }
        public float CommaPause { get => _CommaPause; set { _CommaPause = Mathf.Max(0f, value); RebuildOrder(); } }
        public LoopMode Loop { get => _Loop; set => _Loop = value; }
        public float LoopDelay { get => _LoopDelay; set => _LoopDelay = Mathf.Max(0f, value); }
        public bool PlayOnEnable { get => _PlayOnEnable; set => _PlayOnEnable = value; }
        /// <summary>On (default): ignores Time.timeScale, so UI keeps animating while the game is paused.</summary>
        public bool UnscaledTime { get => _UnscaledTime; set => _UnscaledTime = value; }

        public TMP_Text Text => _tmp != null ? _tmp : _tmp = GetComponent<TMP_Text>();

        public static List<TagEffect> DefaultTagEffects() => new()
        {
            new TagEffect { Tag = "wave", Effect = new WaveEffect() },
            new TagEffect { Tag = "shake", Effect = new ShakeEffect() },
            new TagEffect { Tag = "pulse", Effect = new PulseEffect() },
            new TagEffect { Tag = "rainbow", Effect = new RainbowEffect() },
        };

        // ── playback ───────────────────────────────────────────

        /// <summary>Animates in from hidden.</summary>
        public void Play()
        {
            Progress = 0;
            _direction = 1;
            _delayLeft = _Delay;
            _holdLeft = 0;
            _revealCursor = 0;
            _playing = true;
        }

        /// <summary>Animates out from the current state to hidden.</summary>
        public void Hide()
        {
            _direction = -1;
            _delayLeft = 0;
            _holdLeft = 0;
            _playing = true;
        }

        public void Stop() => _playing = false;

        /// <summary>Jumps to fully shown (a typewriter "skip").</summary>
        public void Complete()
        {
            _playing = false;
            Progress = 1;
            SyncRevealCursor();
        }

        /// <summary>Resets the clock of looping effects (wave, shine…) to zero.</summary>
        public void RestartTime()
        {
            _effectTime = 0;
            _dirty = true;
        }

        public void AddEffect(TextEffect effect)
        {
            _Effects.Add(effect);
            _dirty = true;
        }

        public void SetDirty() => _dirty = true;

        // ── lifecycle ──────────────────────────────────────────

        private void OnEnable()
        {
            _tmp = GetComponent<TMP_Text>();
            if (_tmp == null)
            {
                Debug.LogWarning("[TextAnimator] needs a TextMeshPro text on the same GameObject.", this);
                enabled = false;
                return;
            }
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
            if (_tmp.textPreprocessor == null) _tmp.textPreprocessor = this;

            _effectTime = 0;
            if (_PlayOnEnable && Application.isPlaying) Play();
            _dirty = true;
            _tmp.SetVerticesDirty();
        }

        private void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
#if UNITY_EDITOR
            StopPreview();
#endif
            _playing = false;
            if (_tmp == null) return;
            if (ReferenceEquals(_tmp.textPreprocessor, this)) _tmp.textPreprocessor = null;
            // Hand TMP / LayeredText back their unanimated mesh.
            _tmp.SetVerticesDirty();
        }

        private void OnValidate()
        {
            _dirty = true;
            if (_tmp != null && isActiveAndEnabled) _tmp.SetVerticesDirty();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying && !_previewing)
            {
                // Edit mode: show the serialized progress (Animator/Timeline preview, inspector scrubbing).
                if (_dirty || !Mathf.Approximately(_appliedProgress, _Progress)) ApplyNow();
                return;
            }
            if (_previewing) return; // driven by the editor tick

            Tick(_UnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
        }

        private void Tick(float dt)
        {
            _effectTime += dt;
            Advance(dt);
            if (_direction > 0) AnnounceReveals();

            if (_dirty || !Mathf.Approximately(_appliedProgress, _Progress) || HasLoopingEffect())
                ApplyNow();
        }

        private void Advance(float dt)
        {
            if (!_playing) return;

            if (_delayLeft > 0)
            {
                _delayLeft -= dt;
                if (_delayLeft > 0) return;
                dt = -_delayLeft;
            }

            if (_holdLeft > 0)
            {
                _holdLeft -= dt;
                if (_holdLeft > 0) return;
                dt = -_holdLeft;
                if (_Loop == LoopMode.Loop)
                {
                    Progress = 0;
                    _revealCursor = 0;
                }
                else _direction = -_direction;
            }

            float duration = Mathf.Max(0.0001f, Duration);
            Progress = _Progress + _direction * dt / duration;

            bool atEnd = _direction > 0 ? _Progress >= 1f : _Progress <= 0f;
            if (!atEnd) return;

            if (_Loop == LoopMode.Once || (_direction < 0 && _Loop == LoopMode.Loop))
            {
                _playing = false;
                if (_direction > 0) AnnounceReveals();
                Completed?.Invoke();
                return;
            }
            _holdLeft = Mathf.Max(0.0001f, _LoopDelay);
        }

        private bool HasLoopingEffect()
        {
            if (_textHasTags) return true;
            foreach (var e in _Effects)
            {
                if (e != null && e.Enabled && !e.IsTransition) return true;
            }
            return false;
        }

        // ── reveal events ──────────────────────────────────────

        private void AnnounceReveals()
        {
            if (CharacterRevealed == null) { SyncRevealCursor(); return; }
            float t = _Progress * Duration;
            var info = Text.textInfo;
            while (_revealCursor < _visibleCount && _revealKeys[_revealCursor] <= t)
            {
                int c = _revealOrder[_revealCursor++];
                if (info != null && c < info.characterCount) CharacterRevealed(c, info.characterInfo[c].character);
            }
        }

        /// <summary>Marks everything already on screen as announced, without raising events.</summary>
        private void SyncRevealCursor()
        {
            float t = _Progress * Duration;
            _revealCursor = 0;
            while (_revealCursor < _visibleCount && _revealKeys[_revealCursor] <= t) _revealCursor++;
        }

        // ── text preprocessing (inline tags) ───────────────────

        string ITextPreprocessor.PreprocessText(string text)
        {
            int length = text?.Length ?? 0;
            EnsureSourceCapacity(length);
            _sourceLength = 0;

            if (length == 0 || text.IndexOf('<') < 0)
            {
                _sourceLength = length;
                Array.Clear(_tagMaskBySource, 0, length);
                Array.Clear(_pauseBySource, 0, length);
                return text;
            }

            _preprocessed.Clear();
            int mask = 0;
            float pendingPause = 0;
            for (int i = 0; i < length; i++)
            {
                if (text[i] == '<' && TryParseTag(text, i, out int end, out int bit, out bool closing, out float pause))
                {
                    if (pause >= 0) pendingPause += pause;
                    else if (closing) mask &= ~(1 << bit);
                    else mask |= 1 << bit;
                    i = end;
                    continue;
                }
                int o = _preprocessed.Length;
                _preprocessed.Append(text[i]);
                _tagMaskBySource[o] = mask;
                _pauseBySource[o] = pendingPause;
                pendingPause = 0;
            }
            _sourceLength = _preprocessed.Length;
            return _preprocessed.ToString();
        }

        /// <summary>Recognises &lt;tag&gt;, &lt;/tag&gt; for configured tag effects and &lt;pause=seconds&gt;.</summary>
        private bool TryParseTag(string text, int start, out int end, out int bit, out bool closing, out float pause)
        {
            bit = -1;
            pause = -1;
            closing = false;
            end = text.IndexOf('>', start + 1);
            if (end < 0) return false;

            int nameStart = start + 1;
            if (nameStart < end && text[nameStart] == '/') { closing = true; nameStart++; }
            int eq = text.IndexOf('=', nameStart, end - nameStart);
            int nameEnd = eq >= 0 ? eq : end;
            int nameLength = nameEnd - nameStart;
            if (nameLength <= 0) return false;

            if (!closing && eq >= 0 && Matches(text, nameStart, nameLength, PauseTag))
            {
                string value = text.Substring(eq + 1, end - eq - 1).Trim('"', ' ');
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out pause)) return false;
                pause = Mathf.Max(0, pause);
                return true;
            }

            for (int t = 0; t < _TagEffects.Count && t < MaxTags; t++)
            {
                var tag = _TagEffects[t]?.Tag;
                if (!string.IsNullOrEmpty(tag) && Matches(text, nameStart, nameLength, tag))
                {
                    bit = t;
                    return true;
                }
            }
            return false; // not ours: leave it for TMP
        }

        private static bool Matches(string text, int start, int length, string name) =>
            length == name.Length && string.Compare(text, start, name, 0, length, StringComparison.OrdinalIgnoreCase) == 0;

        private void EnsureSourceCapacity(int length)
        {
            if (_tagMaskBySource.Length >= length) return;
            _tagMaskBySource = new int[length];
            _pauseBySource = new float[length];
        }

        // ── mesh application ───────────────────────────────────

        private bool UsesLayeredMesh
        {
            get
            {
                // Resolved lazily: a LayeredText added after this animator must take over the mesh, otherwise the
                // plain path would push TMP's own vertices over the layered mesh.
                if (_layered == null) TryGetComponent(out _layered);
                return _layered != null && _layered.isActiveAndEnabled && _layered.Style != null;
            }
        }

        private void OnTextChanged(UnityEngine.Object obj)
        {
            if (obj != _tmp || !isActiveAndEnabled) return;

            RebuildOrder();
            if (UsesLayeredMesh)
            {
                _plainModified = false;
                return; // LayeredText builds its mesh right after and queries TryGetState.
            }

            CaptureSource();
            ApplyPlain(); // same frame — no flash of unanimated text after a regeneration
        }

        private void ApplyNow()
        {
            _dirty = false;
            _appliedProgress = _Progress;

            if (UsesLayeredMesh)
            {
                if (_plainModified) { _plainModified = false; _tmp.SetVerticesDirty(); return; }
                _layered.RefreshMesh();
            }
            else
            {
                ApplyPlain();
            }
        }

        /// <summary>
        /// The animated transform for a TMP character index. Returns false when the character is untouched.
        /// Used by <see cref="LayeredText"/> while it builds its layered mesh.
        /// </summary>
        public bool TryGetState(int charIndex, out CharacterState state)
        {
            state = CharacterState.Identity;
            if (!isActiveAndEnabled || charIndex < 0 || charIndex >= _startTime.Length) return false;

            var ctx = new EffectContext
            {
                Index = _revealRank[charIndex],
                TextIndex = _textIndex[charIndex],
                Count = _visibleCount,
                Time = _effectTime,
                Progress = Mathf.Clamp01((_Progress * Duration - _startTime[charIndex]) / _CharDuration),
            };

            foreach (var e in _Effects)
            {
                if (e != null && e.Enabled) e.Apply(ref state, ctx);
            }

            int mask = _tagMask[charIndex];
            for (int t = 0; mask != 0 && t < _TagEffects.Count && t < MaxTags; t++)
            {
                if ((mask & (1 << t)) == 0) continue;
                var e = _TagEffects[t]?.Effect;
                if (e != null && e.Enabled) e.Apply(ref state, ctx);
            }
            return !state.IsIdentity;
        }

        /// <summary>Recomputes per-character start times and tag membership. Called on every regeneration.</summary>
        internal void RebuildOrder()
        {
            var info = Text.textInfo;
            int n = info?.characterCount ?? 0;
            if (_startTime.Length < n)
            {
                _startTime = new float[n];
                _textIndex = new int[n];
                _tagMask = new int[n];
                _revealOrder = new int[n];
                _revealRank = new int[n];
                _revealKeys = new float[n];
            }

            _visibleCount = 0;
            _textHasTags = false;
            for (int i = 0; i < n; i++)
            {
                ref var ci = ref info.characterInfo[i];
                _startTime[i] = 0;
                _revealRank[i] = 0;
                _textIndex[i] = ci.isVisible ? _visibleCount++ : 0;
                int src = ci.index;
                _tagMask[i] = src >= 0 && src < _sourceLength ? _tagMaskBySource[src] : 0;
                if (_tagMask[i] != 0) _textHasTags = true;
            }

            int count = _visibleCount;
            bool readingOrder = _Order == StaggerOrder.LeftToRight || _Order == StaggerOrder.RightToLeft;
            if (_Order == StaggerOrder.Random) ShuffleSlots(count, n * 7919 + count);

            if (readingOrder)
            {
                // Walk in reveal order, accumulating punctuation and <pause> waits into the start times.
                bool reverse = _Order == StaggerOrder.RightToLeft;
                float waited = 0, carried = 0;
                int slot = 0;
                for (int k = 0; k < n; k++)
                {
                    int i = reverse ? n - 1 - k : k;
                    ref var ci = ref info.characterInfo[i];
                    int src = ci.index;
                    if (src >= 0 && src < _sourceLength) carried += _pauseBySource[src];
                    if (!ci.isVisible) continue;

                    waited += carried;
                    carried = 0;
                    _startTime[i] = slot++ * _Stagger + waited;
                    if (slot < count) waited += PunctuationPause(ci.character);
                }
            }
            else
            {
                float mid = (count - 1) * 0.5f;
                for (int i = 0; i < n; i++)
                {
                    if (!info.characterInfo[i].isVisible) continue;
                    int t = _textIndex[i];
                    // Floor, not Round: Mathf.RoundToInt rounds .5 to even, which skips and doubles up slots
                    // (0, 2, 2, 4, ...) when the character count is even.
                    int slot = _Order switch
                    {
                        StaggerOrder.CenterOut => Mathf.FloorToInt(Mathf.Abs(t - mid)),
                        StaggerOrder.EdgesIn => Mathf.FloorToInt(mid - Mathf.Abs(t - mid)),
                        _ => _shuffle[t],
                    };
                    _startTime[i] = slot * _Stagger;
                }
            }

            // Reveal order (visible characters by start time) and the resulting duration.
            int r = 0;
            _duration = 0;
            for (int i = 0; i < n; i++)
            {
                if (!info.characterInfo[i].isVisible) continue;
                _revealOrder[r] = i;
                _revealKeys[r] = _startTime[i];
                r++;
                _duration = Mathf.Max(_duration, _startTime[i] + _CharDuration);
            }
            Array.Sort(_revealKeys, _revealOrder, 0, r);
            for (int k = 0; k < r; k++) _revealRank[_revealOrder[k]] = k;
            SyncRevealCursor();
            _dirty = true;
        }

        private float PunctuationPause(char c) => c switch
        {
            '.' or '!' or '?' or '…' => _SentencePause,
            ',' or ';' or ':' => _CommaPause,
            _ => 0,
        };

        /// <summary>Deterministic Fisher-Yates into a reused buffer (xorshift, so no System.Random allocation).</summary>
        private void ShuffleSlots(int count, int seed)
        {
            if (_shuffle.Length < count) _shuffle = new int[count];
            for (int i = 0; i < count; i++) _shuffle[i] = i;

            uint state = (uint)seed | 1u;
            for (int i = count - 1; i > 0; i--)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                int j = (int)(state % (uint)(i + 1));
                (_shuffle[i], _shuffle[j]) = (_shuffle[j], _shuffle[i]);
            }
        }

        private void CaptureSource()
        {
            var info = _tmp.textInfo;
            int meshCount = info.meshInfo.Length;
            if (_srcVerts.Length < meshCount)
            {
                Array.Resize(ref _srcVerts, meshCount);
                Array.Resize(ref _srcColors, meshCount);
            }
            for (int m = 0; m < meshCount; m++)
            {
                var mi = info.meshInfo[m];
                if (mi.vertices == null) continue;
                if (_srcVerts[m] == null || _srcVerts[m].Length < mi.vertices.Length)
                {
                    _srcVerts[m] = new Vector3[mi.vertices.Length];
                    _srcColors[m] = new Color32[mi.vertices.Length];
                }
                Array.Copy(mi.vertices, _srcVerts[m], mi.vertices.Length);
                Array.Copy(mi.colors32, _srcColors[m], mi.colors32.Length);
            }
            _plainModified = false;
        }

        private void ApplyPlain()
        {
            var info = _tmp.textInfo;
            if (info == null || info.characterCount == 0) return;
            if (_startTime.Length < info.characterCount) RebuildOrder();

            bool any = false;
            for (int c = 0; c < info.characterCount; c++)
            {
                ref var ci = ref info.characterInfo[c];
                if (!ci.isVisible) continue;
                int m = ci.materialReferenceIndex;
                if (m >= _srcVerts.Length || _srcVerts[m] == null) continue;

                var dstV = info.meshInfo[m].vertices;
                var dstC = info.meshInfo[m].colors32;
                var srcV = _srcVerts[m];
                var srcC = _srcColors[m];
                int vi = ci.vertexIndex;
                if (vi + 3 >= srcV.Length || vi + 3 >= dstV.Length) continue;

                if (TryGetState(c, out var st)) any = true;
                var pivot = new Vector3((ci.origin + ci.xAdvance) * 0.5f, ci.baseLine, 0);
                float a = st.Rotation * Mathf.Deg2Rad;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                var offset = (Vector3)(st.Offset * TextUnits.Em(ci));

                for (int k = 0; k < 4; k++)
                {
                    Vector3 d = srcV[vi + k] - pivot;
                    d.x *= st.Scale.x;
                    d.y *= st.Scale.y;
                    dstV[vi + k] = pivot + offset + new Vector3(d.x * cos - d.y * sin, d.x * sin + d.y * cos, d.z);
                    Color col = srcC[vi + k];
                    col *= st.Tint;
                    col = Color.Lerp(col, new Color(1, 1, 1, col.a), st.Flash);
                    col.a *= Mathf.Clamp01(st.Alpha);
                    dstC[vi + k] = col;
                }
            }

            if (!any && !_plainModified) return;
            _plainModified = any;
            _tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
        }

        // ── editor preview ─────────────────────────────────────

#if UNITY_EDITOR
        private double _lastPreviewTime;
        private float _progressBeforePreview;

        public bool IsPreviewing => _previewing;

        public void StartPreview()
        {
            if (Application.isPlaying || _previewing) return;
            _previewing = true;
            _progressBeforePreview = _Progress;
            _effectTime = 0;
            _lastPreviewTime = UnityEditor.EditorApplication.timeSinceStartup;
            UnityEditor.EditorApplication.update += PreviewTick;
            Play();
        }

        public void StopPreview()
        {
            if (!_previewing) return;
            _previewing = false;
            UnityEditor.EditorApplication.update -= PreviewTick;
            _playing = false;
            _effectTime = 0;
            Progress = _progressBeforePreview;
            _dirty = true;
            if (this != null && isActiveAndEnabled) ApplyNow();
        }

        private void PreviewTick()
        {
            if (this == null || !isActiveAndEnabled) { UnityEditor.EditorApplication.update -= PreviewTick; return; }

            double now = UnityEditor.EditorApplication.timeSinceStartup;
            float dt = Mathf.Min(0.1f, (float)(now - _lastPreviewTime));
            _lastPreviewTime = now;

            // Without looping effects or a loop, the preview ends with the transition.
            if (!_playing && _Loop == LoopMode.Once && !HasLoopingEffect())
            {
                StopPreview();
                return;
            }

            Tick(dt);
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditor.SceneView.RepaintAll();
        }
#endif
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Luka.BBEE.KeybindMod;

public sealed class KeybindMenuUi
{
    // Tag-Team inspired dark cyberpunk palette
    private static readonly Color BackdropCol = new(0.02f, 0.03f, 0.06f, 0.72f);
    private static readonly Color PanelCol = new(0.06f, 0.09f, 0.14f, 0.97f);
    private static readonly Color PanelBorderCol = new(0.18f, 0.30f, 0.42f, 1f);
    private static readonly Color CardCol = new(0.09f, 0.14f, 0.21f, 0.96f);
    private static readonly Color CardHoverCol = new(0.12f, 0.19f, 0.28f, 0.98f);
    private static readonly Color CardSelectedCol = new(0.10f, 0.22f, 0.30f, 1f);
    private static readonly Color CardListeningCol = new(0.24f, 0.18f, 0.06f, 1f);
    private static readonly Color CardBorderCol = new(0.20f, 0.32f, 0.44f, 1f);
    private static readonly Color CyanCol = new(0.36f, 0.90f, 0.96f, 1f);
    private static readonly Color AmberCol = new(0.98f, 0.76f, 0.25f, 1f);
    private static readonly Color GreenCol = new(0.40f, 0.88f, 0.56f, 1f);
    private static readonly Color MutedCol = new(0.58f, 0.67f, 0.76f, 1f);
    private static readonly Color TextCol = new(0.93f, 0.96f, 0.99f, 1f);
    private static readonly Color BadgeIdleCol = new(0.13f, 0.18f, 0.26f, 1f);
    private static readonly Color BadgeBoundCol = new(0.12f, 0.36f, 0.44f, 1f);

    private GUIStyle? _solidStyle;
    private GUIStyle? _kickerStyle;
    private GUIStyle? _titleStyle;
    private GUIStyle? _subStyle;
    private GUIStyle? _cardTitleStyle;
    private GUIStyle? _cardSubStyle;
    private GUIStyle? _badgeStyle;
    private GUIStyle? _btnStyle;
    private GUIStyle? _clearStyle;

    private Vector2 _scrollPos = Vector2.zero;

    public int SelectedCharacterId { get; set; } = 101801;
    public bool ShowLockedMoves { get; set; }
    public string? ListeningMoveId { get; private set; }
    public bool WaitingMouseReleaseBeforeListen { get; set; }

    public void BeginListening(string moveId)
    {
        ListeningMoveId = moveId;
        WaitingMouseReleaseBeforeListen = UnityEngine.Input.GetKey(KeyCode.Mouse0);
    }

    public void CancelListening()
    {
        ListeningMoveId = null;
        WaitingMouseReleaseBeforeListen = false;
    }

    public void Draw(
        RuntimeStateSnapshot snapshot,
        BindingStore store,
        KeyCode menuKey,
        bool showHud,
        Action<bool> onToggleHud,
        Action onClose)
    {
        var oldMatrix = GUI.matrix;
        var oldColor = GUI.color;
        var oldBg = GUI.backgroundColor;
        var oldEnabled = GUI.enabled;
        var oldDepth = GUI.depth;
        try
        {
            EnsureStyles();
            GUI.enabled = true;
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.depth = -1000;

            float sw = Screen.width;
            float sh = Screen.height;

            // Fullscreen dim under identity matrix across physical screen
            GUI.matrix = Matrix4x4.identity;
            DrawSolid(new Rect(0f, 0f, sw, sh), BackdropCol);

            // Virtual 1040x680 canvas centered & scaled cleanly for 1080p - 4K
            const float panelW = 1040f;
            const float panelH = 680f;
            float scale = Mathf.Clamp(Math.Min((sw * 0.84f) / panelW, (sh * 0.84f) / panelH), 0.5f, 2.2f);
            float originX = Mathf.Round((sw - panelW * scale) * 0.5f);
            float originY = Mathf.Round((sh - panelH * scale) * 0.5f);
            GUI.matrix = Matrix4x4.TRS(new Vector3(originX, originY, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));

            const float px = 0f;
            const float py = 0f;
            var panelRect = new Rect(px, py, panelW, panelH);

            DrawBox(panelRect, PanelCol, PanelBorderCol, 2f);

            const float pad = 22f;
            float contentW = panelW - pad * 2f;

            // Header
            GUI.Label(new Rect(px + pad, py + 14f, 420f, 18f), "KEYBIND MOD / SKILL KEYBINDS", _kickerStyle!);
            GUI.Label(new Rect(px + pad, py + 32f, 460f, 32f), "Assign Skills & Combos", _titleStyle!);

            string liveLabel = snapshot.InBattle
                ? $"ACTIVE IN RUN: {CharacterCatalog.GetCharacterName(snapshot.ActiveActorId).ToUpperInvariant()}"
                : "OUT OF BATTLE (SELECT ANY FIGHTER TO EDIT)";
            var liveColor = snapshot.InBattle ? CyanCol : MutedCol;
            _subStyle!.normal.textColor = liveColor;
            _subStyle.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(px + panelW - pad - 440f, py + 24f, 385f, 24f), liveLabel, _subStyle);

            if (DrawStyledButton(new Rect(px + panelW - pad - 42f, py + 18f, 42f, 34f), "X", false, false))
            {
                CancelListening();
                onClose();
                ConsumePointerEvent();
                return;
            }

            // Character grid (6 columns, 3 rows for 17 fighters)
            float gridTop = py + 76f;
            int cols = 6;
            float gapX = 8f;
            float gapY = 6f;
            float chipW = (contentW - gapX * (cols - 1)) / cols;
            float chipH = 38f;

            for (int i = 0; i < CharacterCatalog.Roster.Count; i++)
            {
                var fighter = CharacterCatalog.Roster[i];
                int row = i / cols;
                int col = i % cols;
                var r = new Rect(px + pad + col * (chipW + gapX), gridTop + row * (chipH + gapY), chipW, chipH);

                bool isSelectedTab = fighter.ActorId == SelectedCharacterId;
                bool isLiveFighter = snapshot.InBattle && fighter.ActorId == snapshot.ActiveActorId;
                int boundCount = CountBound(fighter.ActorId, store);

                var bg = isSelectedTab ? CardSelectedCol : CardCol;
                var border = isSelectedTab ? CyanCol : (isLiveFighter ? GreenCol : CardBorderCol);
                float bw = (isSelectedTab || isLiveFighter) ? 2f : 1f;

                if (r.Contains(Event.current.mousePosition) && !isSelectedTab)
                    bg = CardHoverCol;

                DrawBox(r, bg, border, bw);

                string shortName = fighter.ActorId == 102801 ? "No.11" : fighter.DisplayName;
                string caption = boundCount > 0 ? $"{shortName} [{boundCount}]" : shortName;
                if (isLiveFighter)
                    caption = "* " + caption;

                _btnStyle!.normal.textColor = isSelectedTab ? CyanCol : (isLiveFighter ? GreenCol : TextCol);
                _btnStyle.fontSize = 13;
                _btnStyle.fontStyle = isSelectedTab ? FontStyle.Bold : FontStyle.Normal;
                if (GUI.Button(r, caption, _btnStyle))
                {
                    CancelListening();
                    SelectedCharacterId = fighter.ActorId;
                    _scrollPos = Vector2.zero;
                }
            }

            int rows = (CharacterCatalog.Roster.Count + cols - 1) / cols;
            float toolbarY = gridTop + rows * (chipH + gapY) + 12f;

            // Filter & Utility Bar
            var allMoves = CharacterCatalog.GetMoves(SelectedCharacterId);
            var visibleMoves = new List<MoveDefinition>();
            for (int i = 0; i < allMoves.Count; i++)
            {
                var m = allMoves[i];
                if (ShowLockedMoves || !snapshot.InBattle || m.IsUnlockedInRun(snapshot))
                    visibleMoves.Add(m);
            }

            if (DrawStyledButton(new Rect(px + pad, toolbarY, 150f, 30f), $"Unlocked Only", !ShowLockedMoves, false))
                ShowLockedMoves = false;
            if (DrawStyledButton(new Rect(px + pad + 158f, toolbarY, 140f, 30f), $"All Moves ({allMoves.Count})", ShowLockedMoves, false))
                ShowLockedMoves = true;
            if (DrawStyledButton(new Rect(px + pad + 308f, toolbarY, 160f, 30f), $"HUD Pill: {(showHud ? "ON" : "OFF")}", showHud, false))
                onToggleHud(!showHud);

            if (DrawStyledButton(new Rect(px + panelW - pad - 170f, toolbarY, 170f, 30f), "Clear Fighter Binds", false, true))
            {
                CancelListening();
                store.ClearCharacterBindings(allMoves);
            }

            // Scrollable 2-column skill card list
            float listY = toolbarY + 40f;
            float footerH = 42f;
            float listH = (py + panelH) - footerH - listY - 8f;
            var viewPort = new Rect(px + pad, listY, contentW, listH);

            int skillCols = 2;
            float sGap = 12f;
            float cardW = (contentW - 22f - sGap) / skillCols;
            float cardH = 74f;
            int sRows = Math.Max(1, (visibleMoves.Count + skillCols - 1) / skillCols);
            float innerH = Math.Max(listH - 4f, sRows * (cardH + sGap));
            var innerRect = new Rect(0f, 0f, contentW - 20f, innerH);

            DrawBox(viewPort, new Color(0.04f, 0.06f, 0.10f, 0.7f), CardBorderCol, 1f);
            _scrollPos = GUI.BeginScrollView(viewPort, _scrollPos, innerRect);

            if (visibleMoves.Count == 0)
            {
                _subStyle.normal.textColor = MutedCol;
                _subStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(new Rect(0, 40f, innerRect.width, 40f), "No unlocked moves detected yet — toggle 'All Moves' to inspect & bind presets.", _subStyle);
            }
            else
            {
                for (int i = 0; i < visibleMoves.Count; i++)
                {
                    var move = visibleMoves[i];
                    int rIdx = i / skillCols;
                    int cIdx = i % skillCols;
                    var cardRect = new Rect(4f + cIdx * (cardW + sGap), 6f + rIdx * (cardH + sGap), cardW, cardH);

                    DrawSkillCard(cardRect, move, allMoves, snapshot, store);
                }
            }

            GUI.EndScrollView();

            // Footer hint bar
            float footY = py + panelH - footerH + 6f;
            _subStyle.normal.textColor = MutedCol;
            _subStyle.alignment = TextAnchor.MiddleLeft;
            string footText = ListeningMoveId != null
                ? "LISTENING: Press any Keyboard key or Mouse button (including Mouse 4 / Mouse 5) to bind  |  Esc: Unassign"
                : $"Click skill card to bind (Keyboard / Mouse 1-7)  |  Esc: Unassign (when binding) / Close  |  {KeyCodeFormatter.Format(menuKey)}: Toggle Menu";
            if (ListeningMoveId != null)
                _subStyle.normal.textColor = AmberCol;

            GUI.Label(new Rect(px + pad, footY, contentW, 28f), footText, _subStyle);

            ConsumePointerEvent();
        }
        finally
        {
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
            GUI.backgroundColor = oldBg;
            GUI.enabled = oldEnabled;
            GUI.depth = oldDepth;
        }
    }

    public void DrawMiniHud(RuntimeStateSnapshot snapshot, BindingStore store)
    {
        var oldMatrix = GUI.matrix;
        var oldColor = GUI.color;
        var oldBg = GUI.backgroundColor;
        var oldEnabled = GUI.enabled;
        var oldDepth = GUI.depth;
        try
        {
            EnsureStyles();
            GUI.enabled = true;
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.depth = -900;

            float scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 2f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            string charName = snapshot.InBattle
                ? CharacterCatalog.GetCharacterName(snapshot.ActiveActorId)
                : "Menu / Hub";
            int bound = snapshot.InBattle ? CountBound(snapshot.ActiveActorId, store) : 0;
            string txt = $"Keybind Mod: {charName} ({bound} bound)  [F4]";

            float vHeight = Screen.height / scale;
            var rect = new Rect(20f, vHeight - 46f, 330f, 26f);
            DrawBox(rect, new Color(0.05f, 0.08f, 0.13f, 0.85f), PanelBorderCol, 1f);
            _subStyle!.normal.textColor = snapshot.InBattle ? CyanCol : MutedCol;
            _subStyle.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(rect.x + 10f, rect.y, rect.width - 16f, rect.height), txt, _subStyle);
        }
        finally
        {
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
            GUI.backgroundColor = oldBg;
            GUI.enabled = oldEnabled;
            GUI.depth = oldDepth;
        }
    }

    private static void ConsumePointerEvent()
    {
        var ev = Event.current;
        if (ev != null && (ev.type == EventType.MouseDown || ev.type == EventType.MouseUp || ev.type == EventType.ScrollWheel))
            ev.Use();
    }

    private void DrawSkillCard(
        Rect rect,
        MoveDefinition move,
        IReadOnlyList<MoveDefinition> allCharacterMoves,
        RuntimeStateSnapshot snapshot,
        BindingStore store)
    {
        bool isListening = ListeningMoveId == move.Id;
        var boundKey = store.GetBinding(move.Id);
        bool isBound = boundKey != KeyCode.None;
        bool isUnlocked = !snapshot.InBattle || move.IsUnlockedInRun(snapshot);
        bool hasDup = isBound && store.GetDuplicateMoves(allCharacterMoves, move.Id, boundKey).Count > 0;

        var bg = isListening ? CardListeningCol : (isBound ? CardSelectedCol : CardCol);
        if (rect.Contains(Event.current.mousePosition) && !isListening)
            bg = CardHoverCol;

        var border = isListening ? AmberCol : (hasDup ? AmberCol : (isBound ? CyanCol : CardBorderCol));
        DrawBox(rect, bg, border, (isListening || isBound) ? 1.5f : 1f);

        // Text on left side
        float badgeW = 136f;
        float textW = rect.width - badgeW - 22f;

        _cardTitleStyle!.normal.textColor = isUnlocked ? TextCol : MutedCol;
        GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, textW, 22f), move.Name, _cardTitleStyle);

        string tag = move.Category switch
        {
            MoveCategory.SpellChant => "SPELL CHANT",
            MoveCategory.HoldLoop => "HOLD LOOP",
            _ => isUnlocked ? "READY" : "LOCKED"
        };
        string sub = $"[{tag}]  {move.ComboHint}";
        _cardSubStyle!.normal.textColor = isUnlocked ? MutedCol : new Color(0.45f, 0.50f, 0.56f, 1f);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 30f, textW, 18f), sub, _cardSubStyle);

        _cardSubStyle.normal.textColor = new Color(0.50f, 0.60f, 0.70f, 1f);
        GUI.Label(new Rect(rect.x + 12f, rect.y + 49f, textW, 18f), move.Description, _cardSubStyle);

        // Right-aligned Keybind badge
        var badgeRect = new Rect(rect.x + rect.width - badgeW - 10f, rect.y + 15f, badgeW, 44f);
        var badgeBg = isListening ? new Color(0.42f, 0.28f, 0.04f, 1f) : (isBound ? BadgeBoundCol : BadgeIdleCol);
        var badgeBorder = isListening ? AmberCol : (hasDup ? AmberCol : (isBound ? CyanCol : CardBorderCol));
        DrawBox(badgeRect, badgeBg, badgeBorder, 1f);

        string badgeText = isListening
            ? "PRESS KEY...\n(ESC = Clear)"
            : KeyCodeFormatter.Format(boundKey);

        _badgeStyle!.normal.textColor = isListening ? AmberCol : (hasDup ? AmberCol : (isBound ? CyanCol : MutedCol));
        _badgeStyle.fontSize = isListening ? 11 : 13;
        GUI.Label(badgeRect, badgeText, _badgeStyle);

        // Transparent invisible button covering entire card for snappy click UX
        if (GUI.Button(rect, string.Empty, _clearStyle!))
        {
            if (isListening)
                CancelListening();
            else
                BeginListening(move.Id);
        }
    }

    private bool DrawStyledButton(Rect rect, string text, bool active, bool danger)
    {
        var bg = active ? BadgeBoundCol : CardCol;
        if (rect.Contains(Event.current.mousePosition))
            bg = CardHoverCol;

        var border = active ? CyanCol : (danger ? new Color(0.75f, 0.35f, 0.35f, 1f) : CardBorderCol);
        DrawBox(rect, bg, border, active ? 1.5f : 1f);

        _btnStyle!.normal.textColor = active ? CyanCol : (danger ? new Color(0.96f, 0.64f, 0.64f, 1f) : TextCol);
        _btnStyle.fontSize = 12;
        _btnStyle.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
        return GUI.Button(rect, text, _btnStyle);
    }

    private static int CountBound(int actorId, BindingStore store)
    {
        int count = 0;
        var moves = CharacterCatalog.GetMoves(actorId);
        for (int i = 0; i < moves.Count; i++)
        {
            if (store.GetBinding(moves[i].Id) != KeyCode.None)
                count++;
        }
        return count;
    }

    private void DrawSolid(Rect r, Color col)
    {
        if (_solidStyle == null)
            return;

        var prevColor = GUI.color;
        var prevBg = GUI.backgroundColor;
        GUI.color = col;
        GUI.backgroundColor = Color.white;
        GUI.Box(r, string.Empty, _solidStyle);
        GUI.backgroundColor = prevBg;
        GUI.color = prevColor;
    }

    private void DrawBox(Rect r, Color fill, Color border, float bw)
    {
        DrawSolid(r, fill);
        DrawSolid(new Rect(r.x, r.y, r.width, bw), border);
        DrawSolid(new Rect(r.x, r.yMax - bw, r.width, bw), border);
        DrawSolid(new Rect(r.x, r.y, bw, r.height), border);
        DrawSolid(new Rect(r.xMax - bw, r.y, bw, r.height), border);
    }

    private void EnsureStyles()
    {
        if (_kickerStyle != null)
            return;

        var whiteTex = Texture2D.whiteTexture;
        _solidStyle = new GUIStyle(GUIStyle.none);
        _solidStyle.normal.background = whiteTex;
        _solidStyle.hover.background = whiteTex;
        _solidStyle.active.background = whiteTex;
        _solidStyle.focused.background = whiteTex;

        _clearStyle = new GUIStyle(GUIStyle.none);

        _kickerStyle = new GUIStyle(GUIStyle.none)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        _kickerStyle.normal.textColor = CyanCol;

        _titleStyle = new GUIStyle(GUIStyle.none)
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        _titleStyle.normal.textColor = TextCol;

        _subStyle = new GUIStyle(GUIStyle.none)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip
        };

        _cardTitleStyle = new GUIStyle(GUIStyle.none)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip
        };

        _cardSubStyle = new GUIStyle(GUIStyle.none)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip
        };

        _badgeStyle = new GUIStyle(GUIStyle.none)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };

        _btnStyle = new GUIStyle(GUIStyle.none)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Clip
        };
    }
}

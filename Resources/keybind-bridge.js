/* BBEE Keybind Mod - Embedded PuerTS Bridge (read-only state, catalog & safe pause helper) */
(function (root) {
    'use strict';

    const FIGHTER_IDS = [
        100101, 100201, 100301, 100401, 100501, 101101, 101401, 101801,
        102201, 102501, 102801, 102901, 103101, 103401, 103501, 103701, 103801
    ];

    function install(requireFn) {
        if (root.__lukaKeybindModV1 && root.__lukaKeybindModV1.installed) {
            return root.__lukaKeybindModV1;
        }

        function safeRequire(path) {
            try { return requireFn(path); } catch (_) { return null; }
        }

        const CS = safeRequire('csharp') || root.CS;
        const X = (safeRequire('Gen/Xlsx') || {}).Xlsx;
        const U = (safeRequire('Core/Util') || {}).U;
        const BattleJsMod = safeRequire('GamePlay/Battle/BattleJs');
        const BattleJs = BattleJsMod ? (BattleJsMod.default || BattleJsMod) : null;
        const InputMgrMod = safeRequire('Manager/TsGameInputManager');
        const InputMgr = InputMgrMod ? (InputMgrMod.default || InputMgrMod) : null;
        const DBMod = safeRequire('DataBinding');
        const DB = DBMod ? (DBMod.DB || DBMod.default) : null;
        const PrepareMod = safeRequire('GamePlay/BattleBreedPrepare/BattlePrepareUtil');
        const Prepare = PrepareMod ? (PrepareMod.default || PrepareMod) : null;
        const RunMod = safeRequire('GamePlay/BattleBreedRun/BattleBreedRun');
        const Run = RunMod ? (RunMod.default || RunMod) : null;

        // Optional UI/Localization modules for resolving action/potential names
        const LangMod = safeRequire('Language/LanguageManager')
            || safeRequire('Manager/LanguageManager')
            || safeRequire('Text/LanguageUtil')
            || safeRequire('Core/Language');

        const pauseState = { added: false, owner: null, battle: null };
        let catalogSent = false;

        function emit(payload) {
            try {
                if (CS && CS.JsManager && CS.JsManager.Instance) {
                    CS.JsManager.Instance.BroadCSEvent('LukaKeybindMod', JSON.stringify(payload));
                }
            } catch (ex) {
                logMsg('WARN', 'BroadCSEvent failed: ' + (ex && ex.message ? ex.message : ex));
            }
        }

        function logMsg(level, text) {
            emit({ kind: 'log', level: level, message: String(text) });
        }

        function readList(list) {
            const out = [];
            if (!list) return out;
            try {
                if (typeof list.Count === 'number' && typeof list.get_Item === 'function') {
                    for (let i = 0; i < list.Count; i++) out.push(list.get_Item(i));
                    return out;
                }
                if (Array.isArray(list) || typeof list.length === 'number') {
                    for (let i = 0; i < list.length; i++) out.push(list[i]);
                    return out;
                }
            } catch (_) { }
            return out;
        }

        function tryText(keyOrId) {
            if (keyOrId === null || keyOrId === undefined || keyOrId === '' || keyOrId === 0) return '';
            try {
                if (U && typeof U.GetText === 'function') {
                    const t = U.GetText(keyOrId);
                    if (typeof t === 'string' && t && t !== String(keyOrId)) return t;
                }
            } catch (_) { }
            try {
                if (LangMod && typeof LangMod.GetText === 'function') {
                    const t = LangMod.GetText(keyOrId);
                    if (typeof t === 'string' && t && t !== String(keyOrId)) return t;
                }
            } catch (_) { }
            return '';
        }

        function tryFindPotentialName(XTables, potentialId) {
            if (!XTables || !potentialId) return '';
            try {
                const pot = XTables.BaseActorPotentialConf ? XTables.BaseActorPotentialConf.Get(potentialId) : null;
                if (pot && pot.triggerInfo && pot.triggerInfo.id) {
                    const td = XTables.PlayerBattleTriggerDetail
                        ? XTables.PlayerBattleTriggerDetail.Get(pot.triggerInfo.id, pot.triggerInfo.level || 1)
                        : null;
                    if (td && typeof td.name === 'string' && td.name) {
                        const loc = tryText(td.name);
                        return loc || td.name;
                    }
                    // Also check BattleTriggerDesc or PotentialDesc keys if present
                    for (const prefix of ['PotentialName_', 'SkillName_', 'TriggerName_']) {
                        const loc = tryText(prefix + potentialId) || tryText(prefix + pot.triggerInfo.id);
                        if (loc) return loc;
                    }
                }
            } catch (_) { }
            return '';
        }

        function sendCatalog(force) {
            if (catalogSent && !force) return;
            const actions = [];
            const instructions = [];
            const xTextTables = [];

            try {
                if (X) {
                    try {
                        for (const k of Object.keys(X)) {
                            if (/text|name|desc|lang|action|potential|skill/i.test(k)) {
                                xTextTables.push(k);
                            }
                        }
                    } catch (_) { }

                    const allActions = readList(X.ActorActionConf && X.ActorActionConf.All);
                    for (const row of allActions) {
                        if (!row || row.close) continue;
                        const buttons = readList(row.buttons).map(b => String(b));
                        if (!buttons.length) continue;

                        const labels = readList(row.label).map(l => String(l));
                        const replaceIds = readList(row.replace).map(n => Number(n) >>> 0);
                        const fromPot = Number(row.fromPotential) || 0;
                        const fromCombo = Number(row.fromPotentialCombo) || 0;

                        let name = '';
                        if (fromPot > 0) name = tryFindPotentialName(X, fromPot);
                        if (!name && labels.length > 0) {
                            for (const lb of labels) {
                                const t = tryText(lb);
                                if (t) { name = t; break; }
                            }
                        }

                        actions.push({
                            id: Number(row.id) >>> 0,
                            actorId: Number(row.actorId) || 0,
                            type: Number(row.type) || 0,
                            buttons: buttons,
                            fromPotential: fromPot >>> 0,
                            fromPotentialCombo: fromCombo >>> 0,
                            replace: replaceIds,
                            isUltra: Boolean(row.isUltraSkill),
                            showInBattle: Boolean(row.showInBattle),
                            sortOrder: Number(row.sortOrder) || 0,
                            labels: labels,
                            name: name
                        });
                    }

                    const allInst = readList(X.ActionInstructionMatchConfig && X.ActionInstructionMatchConfig.All);
                    for (const row of allInst) {
                        if (!row) continue;
                        instructions.push({
                            id: Number(row.id) || 0,
                            instruction: String(row.instruction || ''),
                            successAction: String(row.successAction || ''),
                            actionCd: Number(row.actionCd) || 0,
                            triggerId: Number(row.triggerId) || 0
                        });
                    }
                }
            } catch (ex) {
                logMsg('WARN', 'Catalog build partial error: ' + (ex && ex.message ? ex.message : ex));
            }

            catalogSent = true;
            emit({
                kind: 'catalog',
                actions: actions,
                instructions: instructions,
                xKeys: xTextTables.slice(0, 40)
            });
        }

        function getSnapshot(includeCatalog) {
            if (includeCatalog || !catalogSent) {
                sendCatalog(Boolean(includeCatalog));
            }

            let activeActorId = 0;
            let inBattle = false;
            let inputLocked = false;
            const potentials = [];
            const unlockedFighters = [];

            try {
                if (Prepare && typeof Prepare.GetBaseActorUnlockState === 'function') {
                    for (const fid of FIGHTER_IDS) {
                        try {
                            const st = Prepare.GetBaseActorUnlockState(fid);
                            if (st && st.locked === false) unlockedFighters.push(fid);
                        } catch (_) { }
                    }
                }
            } catch (_) { }

            try {
                const js = BattleJs ? BattleJs.Instance : null;
                const battle = js ? js.Battle : null;
                const player = battle ? battle.PlayerSelf : null;

                if (battle && player) {
                    inBattle = true;
                    activeActorId = Number(player.BaseID) || 0;

                    try {
                        const flag = InputMgr && InputMgr.Instance && InputMgr.Instance.GameInputManager
                            ? Number(InputMgr.Instance.GameInputManager.ActorActionFlag)
                            : 0;
                        const interactable = DB && DB.Input && DB.Input.BattleInteractable
                            ? DB.Input.BattleInteractable.Value !== false
                            : true;
                        const paused = (js.PauseCount || 0) > 0 || (battle.TimeScale || 1) <= 0;
                        inputLocked = flag !== 0 || !interactable || paused;
                    } catch (_) { }

                    let fesActor = null;
                    try { fesActor = typeof js.GetFesActor === 'function' ? js.GetFesActor() : null; } catch (_) { }
                    if (!fesActor) {
                        try { fesActor = Run && Run.SilentInstance && Run.SilentInstance.PlayerNetSt && Run.SilentInstance.PlayerNetSt.MyStatus ? Run.SilentInstance.PlayerNetSt.MyStatus.FesActor : null; } catch (_) { }
                    }

                    if (fesActor && fesActor.potentials) {
                        const pots = readList(fesActor.potentials);
                        for (const p of pots) {
                            const pid = Number(p && (p.id !== undefined ? p.id : p.Id)) >>> 0;
                            if (pid > 0) potentials.push(pid);
                        }
                    }
                }
            } catch (ex) {
                logMsg('WARN', 'Snapshot read failed: ' + (ex && ex.message ? ex.message : ex));
            }

            emit({
                kind: 'state',
                activeActorId: activeActorId,
                inBattle: inBattle,
                inputLocked: inputLocked,
                potentials: potentials,
                unlockedFighters: unlockedFighters
            });
        }

        function setPaused(paused) {
            try {
                const js = BattleJs ? BattleJs.Instance : null;
                const battle = js ? js.Battle : null;
                if (paused) {
                    if (!pauseState.added && js && battle && typeof js.PauseBattle === 'function') {
                        const before = Number(js.PauseCount) || 0;
                        pauseState.owner = js;
                        pauseState.battle = battle;
                        try { js.PauseBattle(true); }
                        finally { pauseState.added = (Number(js.PauseCount) || 0) > before; }
                    }
                } else {
                    const added = pauseState.added;
                    const owner = pauseState.owner;
                    const ownBattle = pauseState.battle;
                    pauseState.added = false;
                    pauseState.owner = null;
                    pauseState.battle = null;
                    if (added && owner && BattleJs && BattleJs.Instance === owner && owner.Battle === ownBattle && (Number(owner.PauseCount) || 0) > 0) {
                        owner.PauseBattle(false);
                    }
                }
            } catch (ex) {
                pauseState.added = false;
                pauseState.owner = null;
                pauseState.battle = null;
                logMsg('WARN', 'setPaused error: ' + (ex && ex.message ? ex.message : ex));
            }
        }

        const api = {
            installed: true,
            sendCatalog: sendCatalog,
            getSnapshot: getSnapshot,
            setPaused: setPaused
        };

        root.__lukaKeybindModV1 = api;
        emit({ kind: 'ack', message: 'Keybind Mod PuerTS bridge installed.' });
        sendCatalog(true);
        getSnapshot(false);
        return api;
    }

    root.__lukaKeybindModInstall = install;
})(globalThis);

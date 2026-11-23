/* BBEE Keybind Mod - Embedded PuerTS Bridge (localized action catalog, active run unlocks & safe pause helper) */
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
        const PrepareMod = safeRequire('GamePlay/BattleBreedPrepare/BattlePrepareUtil');
        const Prepare = PrepareMod ? (PrepareMod.default || PrepareMod) : null;
        const RunMod = safeRequire('GamePlay/BattleBreedRun/BattleBreedRun');
        const Run = RunMod ? (RunMod.default || RunMod) : null;
        const BreedRunBaseMod = safeRequire('GamePlay/BattleBreedRun/BattleBreedRunBase');
        const BreedRunBase = BreedRunBaseMod ? (BreedRunBaseMod.default || BreedRunBaseMod) : null;
        const InputMgrMod = safeRequire('Manager/TsGameInputManager');
        const InputMgr = InputMgrMod ? (InputMgrMod.default || InputMgrMod) : null;
        const DBMod = safeRequire('DataBinding');
        const DB = DBMod ? (DBMod.DB || DBMod.default) : null;
        const DataActorActionMod = safeRequire('Window/Potential/DataActorAction');
        const DataActorActions = DataActorActionMod ? (DataActorActionMod.DataActorActions || DataActorActionMod.default) : null;
        const DetailUtilMod = safeRequire('Window/CurrentActorDetail/CurrentActorDetailUtil');
        const DetailUtil = DetailUtilMod ? (DetailUtilMod.CurrentActorDetailUtil || DetailUtilMod.default) : null;

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

        function getLocMgr() {
            try {
                if (CS && CS.Localization && CS.Localization.LocalizationManager) {
                    return CS.Localization.LocalizationManager.Instance || null;
                }
            } catch (_) { }
            return null;
        }

        function isLocReady() {
            const mgr = getLocMgr();
            if (!mgr) return false;
            try { return Boolean(mgr.IsValid); } catch (_) { return false; }
        }

        function tryLocaleStrict(key) {
            if (key === null || key === undefined || key === '' || key === 0) return '';
            const k = String(key);
            const mgr = getLocMgr();
            if (mgr) {
                try {
                    if (typeof mgr.IsExist === 'function' && !mgr.IsExist(k)) return '';
                    if (typeof mgr.GetString === 'function') {
                        const t = mgr.GetString(k);
                        if (typeof t === 'string' && t && t !== k) return t;
                    }
                } catch (_) { }
            }
            try {
                if (U && typeof U.ExistLocale === 'function' && !U.ExistLocale(k)) return '';
                if (U && typeof U.GetLocale === 'function') {
                    const t = U.GetLocale(k);
                    if (typeof t === 'string' && t && t !== k) return t;
                }
            } catch (_) { }
            return '';
        }

        function cleanDesc(raw) {
            if (!raw || typeof raw !== 'string') return '';
            let s = raw;
            s = s.replace(/<\/?(?:color|size|voffset|font|combo|dynamic)(?:=[^>]*)?>/gi, '');
            s = s.replace(/<br\s*\/?>/gi, ' ');
            s = s.replace(/<action=Summon>\s*<action=Dynamic>/gi, 'SP');
            s = s.replace(/<action=MoveY\+>/gi, 'Up');
            s = s.replace(/<action=MoveY->/gi, 'Down');
            s = s.replace(/<action=MoveX\+>/gi, 'Right');
            s = s.replace(/<action=MoveX->/gi, 'Left');
            s = s.replace(/<action=MoveX>/gi, 'Forward');
            s = s.replace(/<action=MoveY>/gi, 'Up/Down');
            s = s.replace(/<action=MoveX,MoveY>/gi, 'Dir');
            s = s.replace(/<action=Summon>/gi, 'SP');
            s = s.replace(/<action=SpecialAction1>/gi, 'Special');
            s = s.replace(/<action=SpecialAction2>/gi, 'Special 2');
            s = s.replace(/<action=Dynamic>/gi, 'SP');
            s = s.replace(/<action=([^>]+)>/gi, '$1');
            s = s.replace(/\s+/g, ' ').trim();
            return s;
        }

        function buildPotentialMaps() {
            const potentialsByAction = new Map();
            const comboSetsByAction = new Map();
            const potNameById = new Map();

            try {
                const allPots = readList(X && X.BaseActorPotentialConf && X.BaseActorPotentialConf.All);
                for (const p of allPots) {
                    if (!p) continue;
                    const pid = Number(p.id) >>> 0;
                    if (!pid) continue;

                    let pName = '';
                    try {
                        const tid = p.triggerInfo ? Number(p.triggerInfo.id) || 0 : 0;
                        if (tid) {
                            pName = tryLocaleStrict('TriggerName_' + tid);
                            if (!pName && X.PlayerBattleTriggerDetail) {
                                const td = X.PlayerBattleTriggerDetail.Get(tid, p.triggerInfo.level || 1);
                                if (td && typeof td.name === 'string' && td.name) {
                                    pName = tryLocaleStrict(td.name) || td.name;
                                }
                            }
                        }
                    } catch (_) { }
                    if (pName) potNameById.set(pid, cleanDesc(pName));

                    const rels = readList(p.relationId).map(n => Number(n) >>> 0);
                    for (const aid of rels) {
                        if (!aid) continue;
                        if (!potentialsByAction.has(aid)) potentialsByAction.set(aid, []);
                        const arr = potentialsByAction.get(aid);
                        if (!arr.includes(pid)) arr.push(pid);
                    }
                }
            } catch (_) { }

            try {
                const allCombos = readList(X && X.PotentialComboConf && X.PotentialComboConf.All);
                for (const c of allCombos) {
                    if (!c) continue;
                    const reqPots = readList(c.potentials).map(n => Number(n) >>> 0).filter(n => n > 0);
                    const rels = readList(c.relationId).map(n => Number(n) >>> 0);
                    if (!reqPots.length) continue;
                    for (const aid of rels) {
                        if (!aid) continue;
                        if (!comboSetsByAction.has(aid)) comboSetsByAction.set(aid, []);
                        comboSetsByAction.get(aid).push(reqPots);
                    }
                }
            } catch (_) { }

            return { potentialsByAction, comboSetsByAction, potNameById };
        }

        function resolveInheritedName(id, rowMap, actionNameById, potIds, potNameById) {
            const direct = actionNameById.get(id);
            if (direct) return direct;

            const visited = new Set([id]);
            const queue = [];
            const rootRow = rowMap.get(id);
            if (rootRow) {
                for (const inh of rootRow.inherit) queue.push(inh);
            }

            let hops = 0;
            while (queue.length > 0 && hops++ < 8) {
                const cur = queue.shift();
                if (!cur || visited.has(cur)) continue;
                visited.add(cur);

                const nm = actionNameById.get(cur);
                if (nm) return nm;

                const curRow = rowMap.get(cur);
                if (curRow) {
                    for (const inh of curRow.inherit) queue.push(inh);
                }
            }

            if (potIds && potIds.length > 0) {
                for (const pid of potIds) {
                    if (potNameById.has(pid)) return potNameById.get(pid);
                }
            }
            return '';
        }

        function sendCatalog(force) {
            if (catalogSent && !force) return;
            if (!X || !isLocReady()) return;

            const allActions = readList(X.ActorActionConf && X.ActorActionConf.All);
            if (!allActions.length) return;

            const actions = [];
            const instructions = [];

            try {
                const { potentialsByAction, comboSetsByAction, potNameById } = buildPotentialMaps();
                const rowMap = new Map();
                const actionNameById = new Map();

                for (const row of allActions) {
                    if (!row || row.close) continue;
                    const id = Number(row.id) >>> 0;
                    if (!id) continue;

                    const inheritIds = readList(row.inherit).map(n => Number(n) >>> 0).filter(n => n > 0);
                    const replaceIds = readList(row.replace).map(n => Number(n) >>> 0).filter(n => n > 0);
                    rowMap.set(id, { row: row, inherit: inheritIds, replace: replaceIds });

                    const nm = cleanDesc(
                        tryLocaleStrict('ActorActionName_' + id)
                        || tryLocaleStrict('ActorActionName2_' + id)
                    );
                    if (nm) actionNameById.set(id, nm);
                }

                for (const [id, entry] of rowMap.entries()) {
                    const row = entry.row;
                    const type = Number(row.type) || 0;
                    const inputRaw = tryLocaleStrict('ActorActionInput_' + id);
                    if (!inputRaw) continue;

                    const potIds = potentialsByAction.get(id) || [];
                    const comboSets = comboSetsByAction.get(id) || [];
                    const isBase = potIds.length === 0 && comboSets.length === 0;
                    const name = resolveInheritedName(id, rowMap, actionNameById, potIds, potNameById);
                    const labels = readList(row.label).map(l => cleanDesc(tryLocaleStrict(String(l)) || String(l))).filter(Boolean);
                    const desc = cleanDesc(tryLocaleStrict('ActorActionDesc_' + id));

                    actions.push({
                        id: id,
                        actorId: Number(row.actorId) || 0,
                        type: type,
                        name: name,
                        inputRaw: inputRaw,
                        desc: desc,
                        isBase: isBase,
                        potentialIds: potIds,
                        comboPotentialSets: comboSets,
                        replace: entry.replace,
                        inherit: entry.inherit,
                        isUltra: Boolean(row.isUltraSkill),
                        sortOrder: Number(row.sortOrder) || 0,
                        labels: labels
                    });
                }

                // Fallback: if JS localization checks returned empty despite Xlsx rows existing,
                // emit raw rows so C# LocalizationManager.Instance can resolve all strings natively.
                if (actions.length === 0 && rowMap.size > 0) {
                    for (const [id, entry] of rowMap.entries()) {
                        const row = entry.row;
                        const potIds = potentialsByAction.get(id) || [];
                        const comboSets = comboSetsByAction.get(id) || [];
                        actions.push({
                            id: id,
                            actorId: Number(row.actorId) || 0,
                            type: Number(row.type) || 0,
                            name: '',
                            inputRaw: '',
                            desc: '',
                            isBase: potIds.length === 0 && comboSets.length === 0,
                            potentialIds: potIds,
                            comboPotentialSets: comboSets,
                            replace: entry.replace,
                            inherit: entry.inherit,
                            isUltra: Boolean(row.isUltraSkill),
                            sortOrder: Number(row.sortOrder) || 0,
                            labels: []
                        });
                    }
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
            } catch (ex) {
                logMsg('WARN', 'Catalog build partial error: ' + (ex && ex.message ? ex.message : ex));
            }

            if (actions.length > 0) {
                catalogSent = true;
                emit({
                    kind: 'catalog',
                    actions: actions,
                    instructions: instructions
                });
            }
        }

        function resolveFesActor(js) {
            let fesActor = null;
            try { fesActor = js && typeof js.GetFesActor === 'function' ? js.GetFesActor() : null; } catch (_) { }
            if (!fesActor) {
                try {
                    if (DetailUtil && js && js.BattleType) {
                        fesActor = DetailUtil.GetActor(js.BattleType);
                    }
                } catch (_) { }
            }
            if (!fesActor) {
                try {
                    const brb = BreedRunBase && BreedRunBase.SilentInstance;
                    if (brb && brb.dungeon && brb.dungeon.actorInfo) {
                        fesActor = brb.dungeon.actorInfo.fesActor;
                    }
                } catch (_) { }
            }
            if (!fesActor) {
                try {
                    fesActor = Run && Run.SilentInstance && Run.SilentInstance.PlayerNetSt && Run.SilentInstance.PlayerNetSt.MyStatus
                        ? Run.SilentInstance.PlayerNetSt.MyStatus.FesActor
                        : null;
                } catch (_) { }
            }
            return fesActor;
        }

        function computeObtainedActionIds(fesActor, ownedPotSet) {
            const obtained = [];
            if (!fesActor || !fesActor.id) return obtained;

            try {
                if (DataActorActions && typeof DataActorActions.GetActionData === 'function') {
                    const res = DataActorActions.GetActionData(fesActor);
                    if (res && res[0]) {
                        for (const aid of res[0]) {
                            const num = Number(aid) >>> 0;
                            if (num > 0) obtained.push(num);
                        }
                        if (obtained.length > 0) return obtained;
                    }
                }
            } catch (_) { }

            try {
                if (!X || !X.ActorActionConf) return obtained;
                const actorId = Number(fesActor.id) || 0;
                const potRel = new Set();
                const comboRel = new Set();

                for (const p of readList(X.BaseActorPotentialConf && X.BaseActorPotentialConf.All)) {
                    if (!p || Number(p.actorId) !== actorId) continue;
                    for (const rid of readList(p.relationId)) potRel.add(Number(rid) >>> 0);
                }
                const actorCombos = [];
                for (const c of readList(X.PotentialComboConf && X.PotentialComboConf.All)) {
                    if (!c || Number(c.actorId) !== actorId) continue;
                    actorCombos.push(c);
                    for (const rid of readList(c.relationId)) comboRel.add(Number(rid) >>> 0);
                }

                const set = new Set();
                for (const act of readList(X.ActorActionConf.All)) {
                    if (!act || act.close || Number(act.actorId) !== actorId) continue;
                    const id = Number(act.id) >>> 0;
                    if (!potRel.has(id) && !comboRel.has(id)) set.add(id);
                }
                for (const pid of ownedPotSet) {
                    const pConf = X.BaseActorPotentialConf ? X.BaseActorPotentialConf.Get(pid) : null;
                    if (!pConf) continue;
                    for (const rid of readList(pConf.relationId)) set.add(Number(rid) >>> 0);
                }
                for (const c of actorCombos) {
                    const req = readList(c.potentials).map(n => Number(n) >>> 0);
                    if (req.length > 0 && req.every(pid => ownedPotSet.has(pid))) {
                        for (const rid of readList(c.relationId)) set.add(Number(rid) >>> 0);
                    }
                }
                for (const aid of set) {
                    if (aid > 0) obtained.push(aid);
                }
            } catch (_) { }

            return obtained;
        }

        function getSnapshot(includeCatalog) {
            if (includeCatalog || !catalogSent) {
                sendCatalog(Boolean(includeCatalog));
            }

            let activeActorId = 0;
            let fesActorId = 0;
            let inBattle = false;
            let inputLocked = false;
            const potentials = [];
            let obtainedActionIds = [];
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

                    const fesActor = resolveFesActor(js);
                    if (fesActor) {
                        fesActorId = Number(fesActor.id) || 0;
                        const ownedPotSet = new Set();
                        if (fesActor.potentials) {
                            const pots = readList(fesActor.potentials);
                            for (const p of pots) {
                                const pid = Number(p && (p.id !== undefined ? p.id : p.Id)) >>> 0;
                                if (pid > 0) {
                                    potentials.push(pid);
                                    ownedPotSet.add(pid);
                                }
                            }
                        }
                        obtainedActionIds = computeObtainedActionIds(fesActor, ownedPotSet);
                    }
                }
            } catch (ex) {
                logMsg('WARN', 'Snapshot read failed: ' + (ex && ex.message ? ex.message : ex));
            }

            emit({
                kind: 'state',
                activeActorId: activeActorId,
                fesActorId: fesActorId,
                inBattle: inBattle,
                inputLocked: inputLocked,
                potentials: potentials,
                obtainedActionIds: obtainedActionIds,
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

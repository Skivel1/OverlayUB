var Layouts = ["five-medium", "five-big", "three-small-two-big", "three-big-two-small", "small-picks", "truncated-picks", "three-big", "during-initial", "after-initial"];
var Locations = ["top", "bottom"];
var States = ["gone"];

var lolVersion = "";
var championsById = {};
var championsByIndex = [];

function adminData() {
    if (!_S.lolChampSelect)
        _S.lolChampSelect = {};
    if (!_S.lolChampSelect.admin)
        _S.lolChampSelect.admin = {};

    return _S.lolChampSelect.admin;
}

_S.picks = { 1: [], 2: [] };
_S.bans = { 1: [], 2: [] };
_S.pickElements = { 1: [], 2: [] };
_S.banElements = { 1: [], 2: [] };

_S.myTeamBanCount = 0;
_S.theirTeamBanCount = 0;

var initDone = false;
var epoch = 0;
var phaseTimer = 0;
var lastPhase = "";

setInterval(function () {
    phaseTimer -= 100;
    if (phaseTimer < 0)
        phaseTimer = 0;
    SetTimerText(phaseTimer);
}, 100);

function Log(...arr) {
    console.log(...arr);
}

async function ResolveLolVersion() {
    if (adminData().version && adminData().version.trim() !== "") {
        lolVersion = adminData().version.trim();
        return lolVersion;
    }

    try {
        const res = await fetch("https://ddragon.leagueoflegends.com/api/versions.json");
        const versions = await res.json();
        if (Array.isArray(versions) && versions.length > 0) {
            lolVersion = versions[0];
            return lolVersion;
        }
    } catch (err) {
        Log("Failed to fetch latest LoL version, fallback to cached:", err);
    }

    return lolVersion;
}

async function UpdateChampionData() {
    const version = await ResolveLolVersion();
    if (!version) {
        Log("No LoL version available yet for championFull.json");
        return;
    }

    const url = "/assets/cdragon/" + version + "/data/en_US/championFull.json";
    Log("Loading champion data:", url);

    try {
        const res = await fetch(url);
        const out = await res.json();
        const championMap = out.data || {};

        championsById = {};
        championsByIndex = [];

        for (var k in championMap) {
            if (!Object.prototype.hasOwnProperty.call(championMap, k))
                continue;
            const champ = championMap[k];
            championsById[champ.key] = champ;
            championsByIndex.push(champ);
        }
    } catch (err) {
        Log("Error updating champion data:", err);
    }
}

// Connection opened
socket.addEventListener("open", function () {
    SendMessage("Subscribe", "lolChampionData");
});

function SetElementLayout(elem, layout, locat, state, time) {
    var delay = time ? time : 1;
    setTimeout(function () { ReplaceStyles(elem, state, States); }, delay);
    setTimeout(function () { ReplaceStyles(elem, locat, Locations); }, delay);
    setTimeout(function () { ReplaceStyles(elem, layout, Layouts); }, delay);
}

function SetClassLayout(elementClass, layout, locat, state, time) {
    var eles = document.getElementsByClassName(elementClass);
    for (var i = 0; i < eles.length; i++) {
        SetElementLayout(eles[i], layout, locat, state, time);
    }
}

// Listen for messages
socket.addEventListener("message", function (event) {
    var message = JSON.parse(event.data);
    console.log(message);
    if (message.Type == "Update") {
        StateOnUpdate(message);

        if (!initDone && message.Path == "lolChampionData") {
            Main();
        } else if (message.Path == "lolChampSelect/session") {
            UpdatePB();
        } else if (message.Path == "lolChampSelect/admin") {
            if (!lolVersion || (adminData().version && adminData().version !== lolVersion)) {
                UpdateChampionData();
            }
            UpdatePB();
        }
    }
});
//#endregion Socket

function SetTimerText(ms) {
    var timer = document.getElementsByClassName("countdown")[0];
    if (timer) {
        timer.innerHTML = (ms / 1000).toFixed(0);
    }
}

function SetPhaseText(phase) {
    var phaseText = document.getElementsByClassName("phase-title")[0];
    if (phaseText) {
        phaseText.innerHTML = phase;
    }
}

function GetLastInProgressAction() {
    var actions = (_S.lolChampSelect && _S.lolChampSelect.session && _S.lolChampSelect.session.actions) || [];
    for (var i = actions.length - 1; i >= 0; i--) {
        for (var k = 0; k < actions[i].length; k++) {
            var action = actions[i][k];
            if (action && action.isInProgress)
                return action;
        }
    }
    return null;
}

function GetCompletedActions() {
    var actions = (_S.lolChampSelect && _S.lolChampSelect.session && _S.lolChampSelect.session.actions) || [];
    var complete = [];
    for (var i = 0; i < actions.length; i++) {
        for (var k = 0; k < actions[i].length; k++) {
            var action = actions[i][k];
            if (action && action.completed == true) {
                complete.push(action);
            }
        }
    }
    return complete;
}

function GetInProgressActions() {
    var actions = (_S.lolChampSelect && _S.lolChampSelect.session && _S.lolChampSelect.session.actions) || [];
    var inProgress = [];
    for (var i = 0; i < actions.length; i++) {
        for (var k = 0; k < actions[i].length; k++) {
            var action = actions[i][k];
            if (action && action.isInProgress == true) {
                inProgress.push(action);
            }
        }
    }
    return inProgress;
}

function GetTeamActions(allied, type) {
    var actions = (_S.lolChampSelect && _S.lolChampSelect.session && _S.lolChampSelect.session.actions) || [];
    var matched = [];
    for (var i = 0; i < actions.length; i++) {
        for (var k = 0; k < actions[i].length; k++) {
            var action = actions[i][k];
            if (action && action.type == type && action.isAllyAction == allied) {
                matched.push(action);
            }
        }
    }
    return matched;
}

function DeterminePhase(timerPhase, actions) {
    switch (timerPhase) {
        case "BAN_PICK":
            var inProgress = GetLastInProgressAction();
            if (!inProgress)
                return lastPhase || "Waiting";
            return inProgress.type == "ban" ? "Ban Phase" : "Pick Phase";
        case "FINALIZATION":
            return "Final Phase";
        case "GAME_STARTING":
            return "Game Starting";
        case "PLANNING":
            return "Planning";
        default:
            return "Waiting";
    }
}

function UpdatePhase(newPhase) {
    if (lastPhase == newPhase)
        return false;

    console.log(newPhase);
    switch (newPhase) {
        case "Pick Phase":
            if (!_S.lolChampSelect.session.hasSimultaneousPicks && !_S.lolChampSelect.session.hasSimultaneousBans) {
                if (_S.myTeamBanCount <= 3)
                    setTimeout(function () { SetLayout("pro", "p2"); }, 1500);
                else
                    setTimeout(function () { SetLayout("pro", "p4"); }, 1500);
            } else {
                setTimeout(function () { SetLayout("soloqueue", "picks"); }, 1500);
            }
            break;
        case "Ban Phase":
            if (_S.myTeamBanCount > 1)
                setTimeout(function () { SetLayout("pro", "p3"); }, 1500);
            else if (!_S.lolChampSelect.session.hasSimultaneousBans)
                setTimeout(function () { SetLayout("pro", "p1"); }, 1500);
            else
                setTimeout(function () { SetLayout("soloqueue", "bans"); }, 1500);
            break;
        case "Planning":
        case "Game Starting":
            SetLayout("header");
            break;
        case "Waiting":
            SetLayout("reset");
            break;
        default:
            break;
    }

    lastPhase = newPhase;
    SetPhaseText(newPhase);
}

function UpdateTimer(timer) {
    if (timer && typeof timer.adjustedTimeLeftInPhase === "number") {
        phaseTimer = -(Date.now() - timer.internalNowInEpochMs - timer.adjustedTimeLeftInPhase);
    }
}

function UpdateBan(i, teamid, action) {
    var slot = _S.bans[teamid][i];
    if (!slot)
        return;

    if (!action) {
        slot.style = {
            opacity: null,
            backgroundColor: null,
            backgroundImage: null
        };
    } else {
        var champ = action.championId > 0 ? championsById[action.championId] : null;
        slot.style = {
            opacity: action.isInProgress ? 0.8 : 1,
            backgroundColor: champ == null ? (teamid == 1 ? "var(--team-left-color)" : "var(--team-right-color)") : null,
            backgroundImage: champ == null ? null : "url(" + GenerateSquareArtUrl(champ.id) + ")"
        };
    }

    UpdatePath(null, "lolChampSelect/ban/" + teamid + "/" + i, slot);
}

function UpdatePick(i, teamid, team, inProgress) {
    var member = team[i];
    var slot = _S.picks[teamid][i];
    if (!slot)
        return;

    if (!member) {
        slot.reset();
    } else {
        var matchedInProgress = inProgress.filter(function (x) { return x.actorCellId == member.cellId && x.type == "pick"; });
        var inProgressAction = matchedInProgress.length > 0 ? matchedInProgress[0] : null;

        var champ = null;
        if (inProgressAction) {
            champ = inProgressAction.championId > 0 ? championsById[inProgressAction.championId] : null;
        } else {
            champ = member.championId > 0 ? championsById[member.championId] : null;
        }

        var splashArtTransform = null;
        if (champ && _S.lolChampionData && _S.lolChampionData.splash && _S.lolChampionData.splash.horizontal) {
            splashArtTransform = _S.lolChampionData.splash.horizontal[champ.id] || _S.lolChampionData.splash.horizontal["Ahri"];
        }

        slot.flex = inProgressAction ? "3" : null;
        slot.style = {
            opacity: inProgressAction ? 0.8 : 1,
            backgroundColor: inProgressAction && champ == null ? (member.team == 1 ? "var(--team-left-color)" : "var(--team-right-color)") : null,
            backgroundImage: champ == null ? null : "url(" + GenerateSplashArtUrl(champ.id) + ")",
            transform: splashArtTransform ? splashArtTransform.transform : null,
            backgroundPositionX: splashArtTransform ? splashArtTransform.backgroundPositionX : null,
            backgroundPositionY: splashArtTransform ? splashArtTransform.backgroundPositionY : null,
        };

        var customPos = _S.lolChampSelect.admin && _S.lolChampSelect.admin.positions && _S.lolChampSelect.admin.positions[teamid] && _S.lolChampSelect.admin.positions[teamid][i];
        if (customPos && customPos !== "auto") {
            slot.positionBackgroundImage = "url(assets/roles/role-" + customPos + ".png)";
        } else {
            var pos = member.assignedPosition ? member.assignedPosition : "fill";
            slot.positionBackgroundImage = "url(assets/roles/role-" + pos + ".png)";
        }

        slot.summonerName = GetSummonerName(member.summonerId, member.cellId);
        slot.championName = champ ? champ.name : null;
        slot.layout = inProgressAction && champ == null ? "during-initial" : champ != null ? "after-initial" : "";
    }

    UpdatePath(null, "lolChampSelect/pick/" + teamid + "/" + i, slot);
}

function UpdatePB() {
    epoch++;
    var cs = _S.lolChampSelect && _S.lolChampSelect.session;
    if (!cs)
        return;

    var actions = cs.actions || [];
    var myTeam = cs.myTeam || [];
    var theirTeam = cs.theirTeam || [];

    UpdateTimer(cs.timer);
    UpdatePhase(DeterminePhase(cs.timer ? cs.timer.phase : "", actions));

    if (myTeam.length == 0 || actions.length == 0) {
        _S.myTeamBanCount = 0;
        _S.theirTeamBanCount = 0;
        return;
    }

    var inProgress = GetInProgressActions();
    var myTeamId = myTeam[0].team;
    var theirTeamId = myTeamId == 1 ? 2 : 1;

    var myTeamBanActions = GetTeamActions(true, "ban");
    var theirTeamBanActions = GetTeamActions(false, "ban");

    _S.myTeamBanCount = myTeamBanActions.filter(function (x) { return x && x.completed; }).length;
    _S.theirTeamBanCount = theirTeamBanActions.filter(function (x) { return x && x.completed; }).length;

    for (var i = 0; i < 5; i++) {
        UpdatePick(i, myTeamId, myTeam, inProgress);
        UpdatePick(i, theirTeamId, theirTeam, inProgress);
        UpdateBan(i, myTeamId, myTeamBanActions[i]);
        UpdateBan(i, theirTeamId, theirTeamBanActions[i]);
    }
}

//#region DataContext
function GenerateSplashArtUrl(championId) {
    if (!championId)
        return "";
    if (championId === "Fiddlesticks")
        championId = "FiddleSticks";
    return "/assets/cdragon/img/champion/centered/" + championId + "_0.jpg";
}

function GenerateSquareArtUrl(championId) {
    if (!championId)
        return "";
    if (championId === "Fiddlesticks")
        championId = "FiddleSticks";
    return "/assets/cdragon/img/champion/tiles/" + championId + "_0.jpg";
}

function GetSummonerName(summonerId, cellId) {
    var summonerMap = _S.lolChampSelect && _S.lolChampSelect.summoners;
    if (!summonerMap)
        return "N/A";

    if (!summonerId || summonerId == 0 || !summonerMap[summonerId])
        return "Summoner " + (cellId + 1);

    var s = summonerMap[summonerId];
    return s.gameName || s.displayName || ("Summoner " + (cellId + 1));
}

function SetLayout(type, phase) {
    console.log(type, phase);
    if (type == "header")
        SetClassLayout("header", "", "", "");

    if (type == "soloqueue") {
        if (phase == "bans") {
            SetClassLayout("header", "", "", "");
            SetClassLayout("picks", "small-picks", "bottom", "", 500);
            SetClassLayout("bans", "five-big", "top", "gone");
            SetClassLayout("bans", "five-big", "top", "", 500);
        }
        if (phase == "picks") {
            SetClassLayout("header", "", "", "");
            SetClassLayout("bans", "five-big", "top", "gone");
            SetClassLayout("bans", "five-medium", "bottom", "gone", 500);
            SetClassLayout("bans", "five-medium", "bottom", "", 1200);
            SetClassLayout("picks", "", "", "", 500);
        }
    }

    if (type == "pro") {
        if (phase == "p1") {
            SetClassLayout("header", "", "", "");
            SetClassLayout("bans", "three-big", "top", "gone");
            SetClassLayout("bans", "three-big", "top", "", 500);
            SetClassLayout("picks", "", "", "gone");
        }
        if (phase == "p2") {
            SetClassLayout("bans", "five-big", "top", "gone");
            SetClassLayout("bans", "five-medium", "bottom", "gone", 500);
            SetClassLayout("bans", "three-big", "bottom", "", 1200);
            SetClassLayout("picks", "truncated-picks", "", "", 500);
        }
        if (phase == "p3") {
            SetClassLayout("bans", "three-small-two-big", "bottom", "");
        }
        if (phase == "p4") {
            SetClassLayout("bans", "five-medium", "bottom", "");
            SetClassLayout("picks", "", "", "");
        }
    }

    if (type == "reset") {
        SetClassLayout("picks", "three-big", "top", "gone");
        SetClassLayout("bans", "", "", "gone");
        SetClassLayout("header", "", "", "gone");
    }
}

//#endregion Dom Manipulation
function preloadImage(CreationFunc, id) {
    var preloader = document.getElementById("preloader");
    if (!preloader || id >= championsByIndex.length)
        return;

    var splashPreloader = new Image();
    preloader.appendChild(splashPreloader);

    var next = function () {
        preloadImage(CreationFunc, id + 1);
    };

    splashPreloader.addEventListener("load", next);
    splashPreloader.addEventListener("error", next);
    splashPreloader.src = CreationFunc(championsByIndex[id].id);
}

function layoutConverter() {
    return (binding, value) => {
        if (binding.layout != value) {
            ReplaceStyles(binding.dataContext, value, Layouts);
            binding.layout = value;
        }
    };
}

function teamLogoConverter() {
    return defaultConverter(stringFormatConverter("assets/teams/{0}"));
}

function CreateSlot() {
    var _this = {};

    _this.reset = () => {
        for (var prop in _this) {
            if (prop != "reset")
                _this[prop] = null;
        }
        _this.style = {
            opacity: 1,
            backgroundColor: "",
            backgroundImage: "",
            transform: "",
            backgroundPositionX: "",
            backgroundPositionY: "",
        };
    };

    return _this;
}

function Main() {
    for (var i = 0; i < 5; i++) {
        _S.picks[1].push(CreateSlot());
        _S.picks[2].push(CreateSlot());
        _S.bans[1].push(CreateSlot());
        _S.bans[2].push(CreateSlot());
    }

    preloadImage(GenerateSplashArtUrl, 0);
    preloadImage(GenerateSquareArtUrl, 0);

    SendMessage("Subscribe", "lolChampSelect/admin");
    SendMessage("Subscribe", "lolChampSelect/session");
    SendMessage("Subscribe", "lolChampSelect/summoners");

    initDone = true;
}

loadBindings();
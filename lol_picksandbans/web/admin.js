var initDone = false;
var settingsDone = false;

function adminData() {
    if (!_S.lolChampSelect)
        _S.lolChampSelect = {};
    if (!_S.lolChampSelect.admin)
        _S.lolChampSelect.admin = {};

    return _S.lolChampSelect.admin;
}

//#region Socket
function SendMessage(type, content, path) {
    if (!socket || socket.readyState !== WebSocket.OPEN)
        return;

    var message = {
        Type: type,
        Content: type === "Update" ? JSON.stringify(content) : content,
        Path: path
    };
    socket.send(JSON.stringify(message));
}

// Connection opened
socket.addEventListener("open", function () {
    SendMessage("Subscribe", "lolChampionData");
});

// Listen for messages
socket.addEventListener("message", function (event) {
    var message;
    try {
        message = JSON.parse(event.data);
    } catch (err) {
        console.warn("[AdminSocket] Invalid JSON:", err);
        return;
    }

    console.log(message.Path);

    if (message.Type == "Update") {
        StateOnUpdate(message);

        if (!initDone && message.Path == "lolChampionData") {
            Main();
        }

        if (!settingsDone && message.Path == "lolChampSelect/admin") {
            settingsDone = true;

            fetch("https://ddragon.leagueoflegends.com/api/versions.json")
                .then(res => res.json())
                .then((out) => {
                    if (!Array.isArray(out) || out.length === 0)
                        return;

                    var lastSix = out.slice(0, 6);
                    var versionSelector = document.getElementsByClassName("version-select")[0];
                    if (!versionSelector)
                        return;

                    versionSelector.innerHTML = "";
                    for (const version of lastSix) {
                        var opt = document.createElement("option");
                        opt.value = version;
                        opt.innerHTML = version;
                        versionSelector.appendChild(opt);
                    }

                    if (!adminData().version) {
                        adminData().version = lastSix[0];
                    }

                    versionSelector.value = adminData().version;
                    versionSelector.addEventListener("change", function () {
                        adminData().version = versionSelector.value;
                        SendMessage("Update", adminData(), "lolChampSelect/admin");
                    });
                })
                .catch(err => console.warn("[Admin] Failed to load LoL versions:", err));

            var el = document.getElementsByClassName("pick")[0];
            if (el) {
                var enableMove = false;
                el.style.backgroundImage = 'url("/assets/cdragon/img/champion/centered/Ahri_0.jpg")';
                el.style.backgroundPositionX = "0px";
                el.style.backgroundPositionY = "0px";

                var xstart = 0;
                var ystart = 0;
                var xback = 0;
                var yback = 0;

                el.addEventListener("mousedown", (e) => {
                    xstart = e.clientX;
                    ystart = e.clientY;
                    xback = parseInt(el.style.backgroundPositionX, 10) || 0;
                    yback = parseInt(el.style.backgroundPositionY, 10) || 0;
                    enableMove = true;
                });

                el.addEventListener("mousemove", (e) => {
                    if (!enableMove)
                        return;

                    var xdiff = e.clientX - xstart;
                    var ydiff = e.clientY - ystart;

                    el.style.backgroundPositionX = (xback + xdiff) + "px";
                    el.style.backgroundPositionY = (yback + ydiff) + "px";
                });

                el.addEventListener("mouseout", () => {
                    enableMove = false;
                });

                el.addEventListener("mouseup", () => {
                    enableMove = false;
                });
            }
        }
    }
});
//#endregion Socket

function Main() {
    SendMessage("Subscribe", "lolChampSelect/session");
    SendMessage("Subscribe", "lolChampSelect/summoners");
    SendMessage("Subscribe", "lolChampSelect/admin");

    initDone = true;
}

function teamLogoConverter() {
    return defaultConverter(stringFormatConverter("assets/teams/{0}"));
}

loadBindings();
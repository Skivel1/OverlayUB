// Detrás de un reverse proxy el WebSocket se expone en /ws (mismo host, wss si la página es https).
// Al abrir la página directo en el puerto 9345 (uso local) se conserva el puerto 9346.
// El token (?token=...) de la URL de la página se reenvía al WebSocket.
const WS_TOKEN = new URLSearchParams(location.search).get("token") || "";
const WS_URL = (function () {
    var proto = location.protocol === "https:" ? "wss://" : "ws://";
    var base = location.port === "9345"
        ? proto + location.hostname + ":9346"
        : proto + location.host + "/ws";
    return base + (WS_TOKEN ? "?token=" + encodeURIComponent(WS_TOKEN) : "");
})();
let socket = null;
var _S = {};
var bindings = {};

function connectOverlaySocket() {
    socket = new WebSocket(WS_URL);

    socket.addEventListener("open", function () {
        console.log("[OverlaySocket] Connected to", WS_URL);
        SendMessage("Subscribe", "lolChampionData");
        if (typeof initDone !== "undefined" && initDone) {
            SendMessage("Subscribe", "lolChampSelect/admin");
            SendMessage("Subscribe", "lolChampSelect/session");
            SendMessage("Subscribe", "lolChampSelect/summoners");
        }
    });

    socket.addEventListener("message", function (event) {
        let message;
        try {
            message = JSON.parse(event.data);
        } catch (err) {
            console.warn("[OverlaySocket] Invalid JSON message:", err);
            return;
        }

        console.log(message);
        if (message && message.Type === "Update") {
            StateOnUpdate(message);

            if (typeof initDone !== "undefined" && !initDone && message.Path === "lolChampionData" && typeof Main === "function") {
                Main();
            } else if (message.Path === "lolChampSelect/session" && typeof UpdatePB === "function") {
                UpdatePB();
            } else if (message.Path === "lolChampSelect/admin" && typeof UpdateChampionData === "function") {
                UpdateChampionData();
            }
        }
    });

    socket.addEventListener("close", function () {
        console.warn("[OverlaySocket] Disconnected. Reconnecting in 2s...");
        setTimeout(connectOverlaySocket, 2000);
    });

    socket.addEventListener("error", function (err) {
        console.warn("[OverlaySocket] Socket error:", err);
    });
}

connectOverlaySocket();

function parseContentSafe(content) {
    if (content === null || content === undefined)
        return null;
    if (typeof content === "string") {
        if (content.trim() === "")
            return null;
        try {
            return JSON.parse(content);
        } catch {
            return content;
        }
    }
    return content;
}

function StateOnUpdate(message) {
    if (!message || !message.Path)
        return;
    UpdatePath(_S, message.Path, parseContentSafe(message.Content));
}

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

function defaultConverter(converter) {
    return (binding, value) => {
        binding.dataContext[binding.attribute] = converter ? converter(binding, value) : value;
    };
}

function stringFormatConverter(formatString) {
    return (binding, value) => {
        return formatString.replace("{0}", value ?? "");
    };
}

function propertyCopyConverter() {
    return (binding, value) => {
        if (!value || typeof value !== "object")
            return;
        for (var prop in value) {
            if (Object.prototype.hasOwnProperty.call(value, prop)) {
                binding.dataContext[binding.attribute][prop] = value[prop];
            }
        }
    };
}

function cssVariableConverter() {
    return (binding, value) => {
        if (binding.dataContext && typeof binding.dataContext.setProperty === "function") {
            binding.dataContext.setProperty(binding.attribute, value);
        }
    };
}

function eventBroadcaster(event, dataContext, binding) {
    dataContext.addEventListener(event, function () {
        var raw = dataContext[binding.attribute];
        var value = typeof raw === "string" ? raw.replace("<br>", "") : (raw ?? "");
        UpdatePath(dataContext, binding.path, value);
        SendMessage("Update", value, binding.path);
    });
}

function BindSetter(dataContext, bindContext, binding) {
    binding.path = (bindContext || "") + (binding.path || "");

    if (bindings[binding.path] == null)
        bindings[binding.path] = [];

    var setter = binding.in == null
        ? defaultConverter()
        : (typeof window[binding.in] === "function" ? window[binding.in]() : defaultConverter());

    if (binding.out != null) {
        eventBroadcaster(binding.out.length === 0 ? "input" : binding.out, dataContext, binding);
    }

    if (binding.attribute) {
        if (binding.attribute.includes(".")) {
            var splits = binding.attribute.split(".");
            for (var i = 0; i < splits.length - 1; i++) {
                if (dataContext && dataContext[splits[i]] != null) {
                    dataContext = dataContext[splits[i]];
                }
            }
            binding.attribute = splits[splits.length - 1];
        }
    }

    var newBinding = {
        dataContext: dataContext,
        attribute: binding.attribute,
        path: binding.path,
        setter: setter
    };

    newBinding.setValue = function (value) {
        newBinding.setter(newBinding, value);
    };

    bindings[binding.path].push(newBinding);
}

function ResolvePath(_path) {
    var splits = _path.split("/");
    var current = _S;

    for (var i = 0; i < splits.length - 1; i++) {
        var leg = splits[i];
        if (current[leg] == null || typeof current[leg] !== "object") {
            current[leg] = {};
        }
        current = current[leg];
    }

    return {
        object: current,
        attribute: splits[splits.length - 1],
        value: current[splits[splits.length - 1]]
    };
}

function UpdatePath(sender, path, object) {
    var current = ResolvePath(path);
    current.object[current.attribute] = object;

    for (var boundPath in bindings) {
        if (!Object.prototype.hasOwnProperty.call(bindings, boundPath))
            continue;
        if (!boundPath.includes(path))
            continue;

        for (var i = 0; i < bindings[boundPath].length; i++) {
            var binding = bindings[boundPath][i];
            if (binding.dataContext !== sender) {
                var bound = ResolvePath(binding.path);
                if (bound != null && bound.value !== undefined) {
                    binding.setValue(bound.value);
                } else {
                    binding.setValue("");
                }
            }
        }
    }
}

function ReplaceStyles(elem, style, styles) {
    if (!elem || !elem.classList)
        return false;

    var ret = false;
    for (var k = 0; k < styles.length; k++) {
        var cl = styles[k];
        if (style != cl) {
            elem.classList.remove(cl);
            ret = true;
        }
    }

    if (style && style !== "")
        elem.classList.add(style);

    return ret;
}

function getBindContext(elem) {
    if (!elem)
        return "";
    if (elem.bindContext)
        return elem.bindContext;
    return elem.parentElement ? getBindContext(elem.parentElement) : "";
}

function parseNotationDict(value) {
    var dicts = [];
    var bigSplits = value.split("{");

    for (var k = 0; k < bigSplits.length; k++) {
        var bind = bigSplits[k].replace("}", "").trim();
        if (bind.length > 0) {
            var splits = bind.split(" ");
            var dict = {};
            for (var j = 0; j < splits.length; j++) {
                var minisplits = splits[j].split("=");
                if (minisplits.length >= 2) {
                    dict[minisplits[0]] = minisplits[1];
                }
            }
            dicts.push(dict);
        }
    }

    return dicts;
}

function loadBindings() {
    var templatedElements = document.querySelectorAll('[template^="{"]');
    var templateElement = document.querySelectorAll("#templates")[0];

    if (templateElement) {
        for (var i = 0; i < templatedElements.length; i++) {
            var elem = templatedElements[i];
            var dicts = parseNotationDict(elem.attributes.template.nodeValue);
            for (var k = 0; k < dicts.length; k++) {
                var templateInfo = dicts[k];
                var templateSource = document.querySelector("#templates ." + templateInfo.class);
                if (!templateSource)
                    continue;
                var templateHTML = templateSource.outerHTML;

                for (var prop in templateInfo) {
                    if (prop != "class") {
                        var re = new RegExp("{" + prop + "}", "g");
                        templateHTML = templateHTML.replace(re, templateInfo[prop]);
                    }
                }

                elem.outerHTML = templateHTML;
            }

            elem.removeAttribute("template");
        }

        templateElement.remove();
    }

    var bindContexts = document.querySelectorAll('[bind-context^="{"]');
    for (var i = 0; i < bindContexts.length; i++) {
        var elem = bindContexts[i];
        elem.bindContext = elem.attributes["bind-context"].nodeValue.replace("{", "").replace("}", "");
        elem.removeAttribute("bind-context");
    }

    var boundElements = document.querySelectorAll('[bind^="{"]');
    for (var i = 0; i < boundElements.length; i++) {
        var elem = boundElements[i];
        var context = getBindContext(elem);
        var dicts = parseNotationDict(elem.attributes.bind.nodeValue);

        for (var k = 0; k < dicts.length; k++) {
            BindSetter(elem, context, dicts[k]);
        }

        elem.removeAttribute("bind");
    }
}
local json = require('json')

local result = {
    schemaVersion = "fortress-souls-diagnose.v0.1",
    worldLoaded = dfhack.isWorldLoaded(),
    mapLoaded = dfhack.isMapLoaded(),
    siteLoaded = dfhack.isSiteLoaded(),
    tickCount = dfhack.getTickCount()
}

print(json.encode(result, { pretty = false }))
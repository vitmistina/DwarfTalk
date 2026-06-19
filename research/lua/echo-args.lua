local json = require('json')
local args = {...}

print(json.encode({
    schemaVersion = "fortress-souls-echo-args.v0.1",
    argCount = #args,
    args = args
}, { pretty = false }))
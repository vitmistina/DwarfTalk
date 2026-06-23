local json = require('json')

local SCHEMA_VERSION = 'fortress-souls-stock-summary.v0.2'
local MAX_ITEMS_SCANNED = 200000

local PRODUCT_CATEGORY_ORDER = {'drinks', 'preparedFood', 'wood', 'stone'}
local EXCLUSION_ORDER = {'rotten','dumped','forbidden','construction','trader'}

local function safe(fn)
    local ok, value = pcall(fn)
    if ok then return value, nil end
    return nil, tostring(value)
end

local function empty_categories()
    local result = {}
    for _, name in ipairs(PRODUCT_CATEGORY_ORDER) do result[name] = {exact=0} end
    return result
end

local function primary_exclusion(flags)
    if flags.rotten then return 'rotten' end
    if flags.dump then return 'dumped' end
    if flags.forbid then return 'forbidden' end
    if flags.construction then return 'construction' end
    if flags.trader then return 'trader' end
    return nil
end

local function classify(item)
    local type_id = select(1, safe(function() return item:getType() end))
    local type_name = type_id and df.item_type[type_id] or nil
    if type_name == 'DRINK' then return 'drinks' end
    if type_name == 'FOOD' then return 'preparedFood' end
    if type_name == 'WOOD' then return 'wood' end
    if type_name == 'BOULDER' then return 'stone' end
    return nil
end

local function emit(value)
    print(json.encode(value, {pretty=false}))
end

local function main()
    if not dfhack.isMapLoaded() then
        return emit({schemaVersion=SCHEMA_VERSION,error={code='NO_MAP_LOADED',message='DFHack is reachable, but no fortress map is loaded.'}})
    end

    local categories = empty_categories()
    local warnings = {}
    local scanned = 0

    for _, item in ipairs(df.global.world.items.other.IN_PLAY) do
        if scanned >= MAX_ITEMS_SCANNED then
            table.insert(warnings, 'Item scan stopped at the configured limit; totals are incomplete.')
            break
        end

        scanned = scanned + 1
        local quantity = select(1, safe(function() return item:getStackSize() end)) or 1
        if quantity < 0 then quantity = 0 end

        if primary_exclusion(item.flags) == nil then
            local category = classify(item)
            if category ~= nil then
                categories[category].exact = categories[category].exact + quantity
            end
        end
    end

    emit({
        schemaVersion=SCHEMA_VERSION,
        provenance={kind='live-dfhack', generatedBy='fortress-souls/get-stock-summary'},
        gameTime={year=dfhack.world.ReadCurrentYear(), tick=dfhack.world.ReadCurrentTick()},
        categories=categories,
        warnings=warnings
    })
end

local _, err = safe(main)
if err then emit({schemaVersion=SCHEMA_VERSION,error={code='SCRIPT_FAILED',message=err}}) end

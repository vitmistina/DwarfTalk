local json = require('json')

local SCHEMA_VERSION = 'fortress-souls-stock-summary-research.v0.1'
local MAX_ITEMS_SCANNED = 200000

local CATEGORY_ORDER = {
    'preparedFood','drinks','seeds','meat','fish','rawFish','plants','plantGrowths','wood','fuel','cloth','leather',
    'weapons','ammunition','armour','tools','metalBars','stone','furniture','finishedGoods'
}

local ARMOUR_TYPES = {ARMOR=true,PANTS=true,HELM=true,GLOVES=true,SHOES=true,SHIELD=true}
local FURNITURE_TYPES = {
    BED=true,CHAIR=true,TABLE=true,CABINET=true,BOX=true,BIN=true,BARREL=true,DOOR=true,FLOODGATE=true,
    HATCH_COVER=true,GRATE=true,WINDOW=true,STATUE=true,SLAB=true,COFFIN=true,ARMORSTAND=true,WEAPONRACK=true
}
local EXCLUSION_ORDER = {'rotten','dumped','forbidden','construction','trader'}

local function safe(fn)
    local ok, value = pcall(fn)
    if ok then return value, nil end
    return nil, tostring(value)
end

local function scalar(value)
    local kind = type(value)
    if kind == 'string' or kind == 'number' or kind == 'boolean' then return value end
    return nil
end

local function empty_categories()
    local result = {}
    for _, name in ipairs(CATEGORY_ORDER) do result[name] = {exact=0, approximate=nil} end
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
    if type_name == 'FOOD' then return 'preparedFood' end
    if type_name == 'DRINK' then return 'drinks' end
    if type_name == 'SEEDS' then return 'seeds' end
    if type_name == 'MEAT' then return 'meat' end
    if type_name == 'FISH' then return 'fish' end
    if type_name == 'FISH_RAW' then return 'rawFish' end
    if type_name == 'PLANT' then return 'plants' end
    if type_name == 'PLANT_GROWTH' then return 'plantGrowths' end
    if type_name == 'WOOD' then return 'wood' end
    if type_name == 'CLOTH' then return 'cloth' end
    if type_name == 'SKIN_TANNED' then return 'leather' end
    if type_name == 'WEAPON' then return 'weapons' end
    if type_name == 'AMMO' then return 'ammunition' end
    if ARMOUR_TYPES[type_name] then return 'armour' end
    if type_name == 'TOOL' then return 'tools' end
    if type_name == 'BOULDER' then return 'stone' end
    if type_name == 'CRAFTS' then return 'finishedGoods' end
    if FURNITURE_TYPES[type_name] then return 'furniture' end
    if type_name == 'BAR' then
        local mat_type = select(1, safe(function() return item:getMaterial() end))
        if mat_type == df.builtin_mats.COAL then return 'fuel' end
        local mat = select(1, safe(function() return dfhack.matinfo.decode(item) end))
        local is_metal = mat and select(1, safe(function() return mat.material.flags.IS_METAL end))
        if is_metal then return 'metalBars' end
    end
    return nil
end

local function bookkeeping_metadata()
    local plotinfo = df.global.plotinfo
    local precision = select(1, safe(function() return plotinfo.positions.bookkeeper_precision end))
    local settings = select(1, safe(function() return plotinfo.bookkeeper_settings end))
    local root_precision = select(1, safe(function() return plotinfo.bookkeeper_precision end))
    local positions_settings = select(1, safe(function() return plotinfo.positions.bookkeeper_settings end))
    return {
        precision=scalar(precision),
        precisionAvailable=precision ~= nil,
        precisionValueType=type(precision),
        precisionPath='df.global.plotinfo.positions.bookkeeper_precision',
        settings=scalar(settings),
        settingsAvailable=settings ~= nil,
        settingsValueType=type(settings),
        settingsPath='df.global.plotinfo.bookkeeper_settings',
        alternateRootPrecisionAvailable=root_precision ~= nil,
        alternateRootPrecisionValueType=type(root_precision),
        alternateRootPrecisionPath='df.global.plotinfo.bookkeeper_precision',
        alternatePositionsSettingsAvailable=positions_settings ~= nil,
        alternatePositionsSettingsValueType=type(positions_settings),
        alternatePositionsSettingsPath='df.global.plotinfo.positions.bookkeeper_settings',
        directGameEstimateVerified=false,
        note='The installed DFHack API did not establish a stable direct mapping from bookkeeping metadata to the visible rounded stock estimate; approximate values are therefore null.'
    }
end

local function emit(value)
    print(json.encode(value, {pretty=false}))
end

local function main()
    if not dfhack.isMapLoaded() then
        return emit({schemaVersion=SCHEMA_VERSION,error={code='NO_MAP_LOADED',message='DFHack is reachable, but no fortress map is loaded.'}})
    end

    local categories = empty_categories()
    local excluded_by_reason = {}
    for _, reason in ipairs(EXCLUSION_ORDER) do excluded_by_reason[reason] = {itemObjects=0,quantity=0} end
    local totals = {itemObjects=0,quantity=0,usableItemObjects=0,usableQuantity=0,excludedItemObjects=0,excludedQuantity=0,categorizedQuantity=0,uncategorizedQuantity=0}
    local context = {containedItemObjects=0,ownedItemObjects=0,inInventoryItemObjects=0}
    local warnings = {}

    for _, item in ipairs(df.global.world.items.other.IN_PLAY) do
        if totals.itemObjects >= MAX_ITEMS_SCANNED then
            table.insert(warnings, 'Item scan stopped at the configured limit; totals are incomplete.')
            break
        end
        local quantity = select(1, safe(function() return item:getStackSize() end)) or 1
        if quantity < 0 then quantity = 0 end
        totals.itemObjects = totals.itemObjects + 1
        totals.quantity = totals.quantity + quantity
        local container = select(1, safe(function() return dfhack.items.getContainer(item) end))
        if container ~= nil then context.containedItemObjects = context.containedItemObjects + 1 end
        if select(1, safe(function() return item.flags.owned end)) then context.ownedItemObjects = context.ownedItemObjects + 1 end
        if select(1, safe(function() return item.flags.in_inventory end)) then context.inInventoryItemObjects = context.inInventoryItemObjects + 1 end

        local reason = primary_exclusion(item.flags)
        if reason ~= nil then
            totals.excludedItemObjects = totals.excludedItemObjects + 1
            totals.excludedQuantity = totals.excludedQuantity + quantity
            excluded_by_reason[reason].itemObjects = excluded_by_reason[reason].itemObjects + 1
            excluded_by_reason[reason].quantity = excluded_by_reason[reason].quantity + quantity
        else
            totals.usableItemObjects = totals.usableItemObjects + 1
            totals.usableQuantity = totals.usableQuantity + quantity
            local category = classify(item)
            if category ~= nil then
                categories[category].exact = categories[category].exact + quantity
                totals.categorizedQuantity = totals.categorizedQuantity + quantity
            else
                totals.uncategorizedQuantity = totals.uncategorizedQuantity + quantity
            end
        end
    end

    emit({
        schemaVersion=SCHEMA_VERSION,
        provenance={kind='live-dfhack', generatedBy='fortress-souls/research-stock-summary'},
        environment={dfVersion=dfhack.getDFVersion(),dfhackVersion=dfhack.getDFHackVersion(),dfhackGitDescription=dfhack.getGitDescription()},
        gameTime={year=dfhack.world.ReadCurrentYear(),tick=dfhack.world.ReadCurrentTick()},
        source='df.global.world.items.other.IN_PLAY',
        definitions={
            total='All scanned IN_PLAY item objects and their stack quantities, before exclusions.',
            usable='Items not rotten, dumped, forbidden, construction, or trader; ownership, containment, and in-inventory state are reported but do not change the dfstatus-compatible baseline.',
            excluded='Items assigned to the first matching exclusion reason in deterministic priority order.',
            category='Each usable item contributes its stack size to at most one category. Containers are not added to the category of their contents.'
        },
        limits={maxItemsScanned=MAX_ITEMS_SCANNED}, exclusionPriority=EXCLUSION_ORDER,
        totals=totals, excludedByReason=excluded_by_reason, categories=categories, context=context,
        bookkeeping=bookkeeping_metadata(), warnings=warnings,
        unsupported={
            'Visible game-style approximate counts are not emitted until the bookkeeping rounding algorithm is verified against the live UI.',
            'Uncategorized usable quantity is retained instead of guessing a category from descriptions.',
            'Squad-equipment assignment was not reliably distinguished from general in-inventory state and does not drive exclusions.'
        }
    })
end

local _, err = safe(main)
if err then emit({schemaVersion=SCHEMA_VERSION,error={code='SCRIPT_FAILED',message=err}}) end

local json = require('json')

local function safe(fn)
    local ok, value = pcall(fn)
    if ok then
        return value, nil
    end
    return nil, value
end

local function json_scalar(value)
    local value_type = type(value)
    if value == nil then return nil end
    if value_type == "string" or value_type == "number" or value_type == "boolean" then
        return value
    end
    return tostring(value)
end

local function enum_name(enum_table, value)
    if enum_table == nil or value == nil then return nil end

    local result, err = safe(function()
        return enum_table[value]
    end)

    if err ~= nil then return nil end
    return json_scalar(result)
end

local function read_field(obj, field_name)
    if obj == nil then return nil, nil end

    local value, err = safe(function()
        return obj[field_name]
    end)

    if err ~= nil then return nil, err end
    return value, nil
end

local function pos_to_json(pos)
    if pos == nil then return nil end

    local x = select(1, read_field(pos, "x"))
    local y = select(1, read_field(pos, "y"))
    local z = select(1, read_field(pos, "z"))

    if x == nil and y == nil and z == nil then
        return nil
    end

    local is_valid = not (x == -30000 and y == -30000 and z == -30000)

    return {
        x = json_scalar(x),
        y = json_scalar(y),
        z = json_scalar(z),
        isValid = is_valid
    }
end

local function get_current_job_token(unit)
    local job = nil

    if unit.job ~= nil then
        job = unit.job.current_job
    end

    if job == nil then
        return nil
    end

    return enum_name(df.job_type, job.job_type)
end

local function summarize_citizen(unit)
    local profession_id = select(1, safe(function()
        return dfhack.units.getProfession(unit)
    end))

    local stress_category = select(1, safe(function()
        return dfhack.units.getStressCategory(unit)
    end))

    local readable_name = select(1, safe(function()
        return dfhack.units.getReadableName(unit)
    end))

    local profession_name = select(1, safe(function()
        return dfhack.units.getProfessionName(unit)
    end))

    local soul = nil
    if unit.status ~= nil then
        soul = unit.status.current_soul
    end

    local creature_id = select(1, safe(function()
        local creature = df.global.world.raws.creatures.all[unit.race]
        return creature.creature_id
    end))

    local caste_id = select(1, safe(function()
        local creature = df.global.world.raws.creatures.all[unit.race]
        return creature.caste[unit.caste].caste_id
    end))

    return {
        id = tostring(unit.id),
        rawId = json_scalar(unit.id),
        displayName = json_scalar(readable_name),
        professionName = json_scalar(profession_name),
        professionId = json_scalar(profession_id),
        professionToken = enum_name(df.profession, profession_id),
        currentJobType = get_current_job_token(unit),
        stressCategory = json_scalar(stress_category),
        stressCategoryScale = "0-most-stressed-6-least-stressed",
        soulPresent = soul ~= nil,
        histFigureId = json_scalar(unit.hist_figure_id),
        race = json_scalar(unit.race),
        caste = json_scalar(unit.caste),
        creatureId = json_scalar(creature_id),
        casteId = json_scalar(caste_id),
        position = pos_to_json(unit.pos),
        flags = {
            isCitizen = json_scalar(select(1, safe(function() return dfhack.units.isCitizen(unit, true) end))),
            isResident = json_scalar(select(1, safe(function() return dfhack.units.isResident(unit, true) end))),
            isSane = json_scalar(select(1, safe(function() return dfhack.units.isSane(unit) end))),
            isAlive = json_scalar(select(1, safe(function() return dfhack.units.isAlive(unit) end))),
            isActive = json_scalar(select(1, safe(function() return dfhack.units.isActive(unit) end)))
        }
    }
end

local function main(...)
    local result = {
        schemaVersion = "fortress-souls-dwarf-list.v0.1",
        worldLoaded = dfhack.isWorldLoaded(),
        mapLoaded = dfhack.isMapLoaded(),
        siteLoaded = dfhack.isSiteLoaded(),
        tickCount = dfhack.getTickCount(),
        items = {}
    }

    if not dfhack.isMapLoaded() then
        result.error = {
            code = "NO_MAP_LOADED",
            message = "DFHack is reachable, but no fortress map is loaded."
        }

        print(json.encode(result, { pretty = false }))
        return
    end

    local citizens = dfhack.units.getCitizens()

    for _, unit in ipairs(citizens) do
        local item, err = safe(function()
            return summarize_citizen(unit)
        end)

        if err == nil then
            table.insert(result.items, item)
        else
            table.insert(result.items, {
                id = tostring(unit.id),
                error = tostring(err)
            })
        end
    end

    result.count = #result.items

    print(json.encode(result, { pretty = false }))
end

main(...)

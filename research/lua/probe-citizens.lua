local json = require('json')

local function safe(label, fn)
    local ok, value = pcall(fn)
    if ok then
        return value, nil
    end

    return nil, tostring(value)
end

local function enum_name(enum_table, value)
    if value == nil then
        return nil
    end

    local ok, name = pcall(function()
        return enum_table[value]
    end)

    if ok then
        return name
    end

    return nil
end

local function name_to_string(language_name)
    if language_name == nil then
        return nil
    end

    local ok, value = pcall(function()
        return dfhack.TranslateName(language_name)
    end)

    if ok then
        return value
    end

    return nil
end

local function summarize_unit(unit)
    local visible_name, visible_name_error = safe("visibleName", function()
        return name_to_string(dfhack.units.getVisibleName(unit))
    end)

    local readable_name, readable_name_error = safe("readableName", function()
        return dfhack.units.getReadableName(unit)
    end)

    local profession_name, profession_name_error = safe("professionName", function()
        return dfhack.units.getProfessionName(unit)
    end)

    local profession_id, profession_id_error = safe("professionId", function()
        return dfhack.units.getProfession(unit)
    end)

    local stress_category, stress_category_error = safe("stressCategory", function()
        return dfhack.units.getStressCategory(unit)
    end)

    local raw_stress, raw_stress_error = safe("rawStress", function()
        if unit.status and unit.status.current_soul and unit.status.current_soul.personality then
            return unit.status.current_soul.personality.stress
        end
        return nil
    end)

    local soul_present, soul_present_error = safe("soulPresent", function()
        return unit.status ~= nil and unit.status.current_soul ~= nil
    end)

    local current_job_type, current_job_error = safe("currentJobType", function()
        if unit.job and unit.job.current_job then
            return enum_name(df.job_type, unit.job.current_job.job_type)
        end
        return nil
    end)

    return {
        id = tostring(unit.id),
        rawId = unit.id,

        visibleName = visible_name,
        readableName = readable_name,

        professionId = profession_id,
        professionToken = enum_name(df.profession, profession_id),
        professionName = profession_name,

        stressCategory = stress_category,
        rawStress = raw_stress,

        soulPresent = soul_present,

        currentJobType = current_job_type,

        flags = {
            isCitizen = dfhack.units.isCitizen(unit, true),
            isResident = dfhack.units.isResident(unit, true),
            isSane = dfhack.units.isSane(unit),
            isAlive = dfhack.units.isAlive(unit),
            isDwarf = dfhack.units.isDwarf(unit),
            isActive = dfhack.units.isActive(unit)
        },

        errors = {
            visibleName = visible_name_error,
            readableName = readable_name_error,
            professionName = profession_name_error,
            professionId = profession_id_error,
            stressCategory = stress_category_error,
            rawStress = raw_stress_error,
            soulPresent = soul_present_error,
            currentJobType = current_job_error
        }
    }
end

local function main(...)
    local args = {...}
    local limit = tonumber(args[1]) or 10

    local result = {
        schemaVersion = "fortress-souls-probe-citizens.v0.1",
        worldLoaded = dfhack.isWorldLoaded(),
        mapLoaded = dfhack.isMapLoaded(),
        siteLoaded = dfhack.isSiteLoaded(),
        tickCount = dfhack.getTickCount(),
        limit = limit,
        citizens = {}
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
    result.totalCitizens = #citizens

    for index, unit in ipairs(citizens) do
        if index > limit then
            break
        end

        table.insert(result.citizens, summarize_unit(unit))
    end

    print(json.encode(result, { pretty = false }))
end

main(...)
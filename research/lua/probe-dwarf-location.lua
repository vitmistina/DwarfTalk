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

    if value == nil then
        return nil
    end

    if value_type == "string" or value_type == "number" or value_type == "boolean" then
        return value
    end

    return tostring(value)
end

local function enum_name(enum_table, value)
    if enum_table == nil or value == nil then
        return nil
    end

    local result, err = safe(function()
        return enum_table[value]
    end)

    if err ~= nil then
        return nil
    end

    return json_scalar(result)
end

local function read_field(obj, field_name)
    if obj == nil then
        return nil, nil
    end

    local value, err = safe(function()
        return obj[field_name]
    end)

    if err ~= nil then
        return nil, err
    end

    return value, nil
end

local function read_path(root, path)
    local current = root

    for _, part in ipairs(path) do
        if current == nil then
            return nil, nil
        end

        local value, err = read_field(current, part)

        if err ~= nil then
            return nil, err
        end

        current = value
    end

    return current, nil
end

local function pos_to_json(pos)
    if pos == nil then
        return nil
    end

    local x = select(1, read_field(pos, "x"))
    local y = select(1, read_field(pos, "y"))
    local z = select(1, read_field(pos, "z"))

    if x == nil and y == nil and z == nil then
        return {
            raw = json_scalar(pos),
            readStatus = "no-x-y-z-fields"
        }
    end

    return {
        x = json_scalar(x),
        y = json_scalar(y),
        z = json_scalar(z)
    }
end

local function add_error(errors, key, err)
    if err ~= nil then
        errors[key] = tostring(err)
    end
end

local function maybe_errors(errors)
    if errors ~= nil and next(errors) then
        return errors
    end

    return nil
end

local function find_citizen_by_id(unit_id)
    local citizens = dfhack.units.getCitizens()

    for _, unit in ipairs(citizens) do
        if tostring(unit.id) == tostring(unit_id) then
            return unit
        end
    end

    return nil
end

local function get_readable_name(unit)
    local value, err = safe(function()
        return dfhack.units.getReadableName(unit)
    end)

    if err ~= nil then
        return nil
    end

    return value
end

local function get_profession_name(unit)
    local value, err = safe(function()
        return dfhack.units.getProfessionName(unit)
    end)

    if err ~= nil then
        return nil
    end

    return value
end

local function summarize_job(unit)
    local errors = {}

    local job, job_err = read_path(unit, {"job", "current_job"})
    add_error(errors, "currentJob", job_err)

    if job == nil then
        return {
            present = false,
            errors = maybe_errors(errors)
        }
    end

    local job_type, job_type_err = read_field(job, "job_type")
    local job_id, job_id_err = read_field(job, "id")
    local job_pos, job_pos_err = read_field(job, "pos")
    local completion_timer, completion_timer_err = read_field(job, "completion_timer")

    add_error(errors, "jobType", job_type_err)
    add_error(errors, "jobId", job_id_err)
    add_error(errors, "jobPos", job_pos_err)
    add_error(errors, "completionTimer", completion_timer_err)

    return {
        present = true,
        id = json_scalar(job_id),
        type = json_scalar(job_type),
        token = enum_name(df.job_type, job_type),
        pos = pos_to_json(job_pos),
        completionTimer = json_scalar(completion_timer),
        errors = maybe_errors(errors)
    }
end

local function summarize_building(building)
    if building == nil then
        return nil
    end

    local building_type = select(1, read_field(building, "getType"))

    local type_value, type_err = safe(function()
        return building:getType()
    end)

    local name_value, name_err = safe(function()
        if dfhack.buildings and dfhack.buildings.getName then
            return dfhack.buildings.getName(building)
        end

        return nil
    end)

    local id = select(1, read_field(building, "id"))
    local subtype = select(1, read_field(building, "subtype"))
    local custom_type = select(1, read_field(building, "custom_type"))
    local centerx = select(1, read_field(building, "centerx"))
    local centery = select(1, read_field(building, "centery"))
    local z = select(1, read_field(building, "z"))
    local x1 = select(1, read_field(building, "x1"))
    local x2 = select(1, read_field(building, "x2"))
    local y1 = select(1, read_field(building, "y1"))
    local y2 = select(1, read_field(building, "y2"))

    local errors = {}

    add_error(errors, "getType", type_err)
    add_error(errors, "getName", name_err)

    return {
        id = json_scalar(id),
        type = json_scalar(type_value),
        typeToken = enum_name(df.building_type, type_value),
        subtype = json_scalar(subtype),
        customType = json_scalar(custom_type),
        name = json_scalar(name_value),
        center = {
            x = json_scalar(centerx),
            y = json_scalar(centery),
            z = json_scalar(z)
        },
        bounds = {
            x1 = json_scalar(x1),
            x2 = json_scalar(x2),
            y1 = json_scalar(y1),
            y2 = json_scalar(y2),
            z = json_scalar(z)
        },
        errors = maybe_errors(errors)
    }
end

local function find_building_at_pos(pos)
    local errors = {}

    if pos == nil then
        return nil, {
            noPosition = "No position available."
        }
    end

    local building = nil

    if dfhack.buildings and dfhack.buildings.findAtTile then
        local b1, b1_err = safe(function()
            return dfhack.buildings.findAtTile(pos)
        end)

        if b1 ~= nil then
            building = b1
        else
            add_error(errors, "findAtTilePosObject", b1_err)

            local x = select(1, read_field(pos, "x"))
            local y = select(1, read_field(pos, "y"))
            local z = select(1, read_field(pos, "z"))

            local b2, b2_err = safe(function()
                return dfhack.buildings.findAtTile(x, y, z)
            end)

            if b2 ~= nil then
                building = b2
            else
                add_error(errors, "findAtTileXyz", b2_err)
            end
        end
    else
        errors.findAtTile = "dfhack.buildings.findAtTile is not available."
    end

    return building, maybe_errors(errors)
end

local function summarize_location(unit)
    local errors = {}

    local pos, pos_err = read_field(unit, "pos")
    local path_dest, path_dest_err = read_path(unit, {"path", "dest"})
    local idle_area, idle_area_err = read_field(unit, "idle_area")
    local idle_area_type, idle_area_type_err = read_field(unit, "idle_area_type")
    local burrow_id, burrow_id_err = read_field(unit, "burrow_id")

    add_error(errors, "pos", pos_err)
    add_error(errors, "pathDest", path_dest_err)
    add_error(errors, "idleArea", idle_area_err)
    add_error(errors, "idleAreaType", idle_area_type_err)
    add_error(errors, "burrowId", burrow_id_err)

    local building, building_errors = find_building_at_pos(pos)

    if building_errors ~= nil then
        errors.buildingAtPos = building_errors
    end

    return {
        pos = pos_to_json(pos),
        pathDestination = pos_to_json(path_dest),
        idleArea = pos_to_json(idle_area),
        idleAreaType = json_scalar(idle_area_type),
        burrowId = json_scalar(burrow_id),
        buildingAtPos = summarize_building(building),
        errors = maybe_errors(errors)
    }
end

local function build_snapshot(unit_id)
    local result = {
        schemaVersion = "fortress-souls-probe-dwarf-location.v0.1",
        worldLoaded = dfhack.isWorldLoaded(),
        mapLoaded = dfhack.isMapLoaded(),
        siteLoaded = dfhack.isSiteLoaded(),
        tickCount = dfhack.getTickCount(),
        requestedUnitId = unit_id
    }

    if not dfhack.isMapLoaded() then
        result.error = {
            code = "NO_MAP_LOADED",
            message = "DFHack is reachable, but no fortress map is loaded."
        }

        return result
    end

    if unit_id == nil then
        result.error = {
            code = "MISSING_UNIT_ID",
            message = "Expected unit id argument."
        }

        return result
    end

    local unit = find_citizen_by_id(unit_id)

    if unit == nil then
        result.error = {
            code = "UNIT_NOT_FOUND",
            message = "No current citizen found with requested unit id."
        }

        return result
    end

    result.identity = {
        id = tostring(unit.id),
        readableName = json_scalar(get_readable_name(unit)),
        professionName = json_scalar(get_profession_name(unit)),
        histFigureId = json_scalar(select(1, read_field(unit, "hist_figure_id")))
    }

    result.location = summarize_location(unit)
    result.job = summarize_job(unit)

    return result
end

local function emit(obj)
    local encoded, encode_err = safe(function()
        return json.encode(obj, { pretty = false })
    end)

    if encode_err ~= nil then
        print(json.encode({
            schemaVersion = "fortress-souls-probe-dwarf-location.v0.1",
            error = {
                code = "JSON_ENCODE_FAILED",
                message = tostring(encode_err)
            }
        }, { pretty = false }))

        return
    end

    print(encoded)
end

local function main(...)
    local args = {...}
    local unit_id = args[1]

    local snapshot, err = safe(function()
        return build_snapshot(unit_id)
    end)

    if err ~= nil then
        emit({
            schemaVersion = "fortress-souls-probe-dwarf-location.v0.1",
            requestedUnitId = unit_id,
            error = {
                code = "SCRIPT_FAILED",
                message = tostring(err)
            }
        })

        return
    end

    emit(snapshot)
end

main(...)

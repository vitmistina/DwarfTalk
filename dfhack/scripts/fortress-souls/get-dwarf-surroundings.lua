local json = require('json')

local SCHEMA_VERSION = 'fortress-souls-dwarf-surroundings.v0.2'
local DEFAULT_RADIUS = 1
local MAX_RADIUS = 2
local MAX_UNITS_SCANNED = 10000

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

local function walkable_flag(value)
    local scalar_value = scalar(value)
    if type(scalar_value) == 'boolean' then return scalar_value end
    if type(scalar_value) == 'number' then return scalar_value > 0 end
    return nil
end

local function enum_name(enum, value)
    if enum == nil or value == nil then return nil end
    return scalar(select(1, safe(function() return enum[value] end)))
end

local function integer(value, name, minimum, maximum)
    local parsed = tonumber(value)
    if parsed == nil or parsed ~= math.floor(parsed) then
        return nil, name .. ' must be an integer.'
    end
    if parsed < minimum or parsed > maximum then
        return nil, name .. ' must be between ' .. minimum .. ' and ' .. maximum .. '.'
    end
    return parsed, nil
end

local function position(value)
    if value == nil then return nil end
    local x = scalar(select(1, safe(function() return value.x end)))
    local y = scalar(select(1, safe(function() return value.y end)))
    local z = scalar(select(1, safe(function() return value.z end)))
    if x == nil or y == nil or z == nil or x == -30000 then return nil end
    return {x=x, y=y, z=z}
end

local function read_position(fn)
    local ok, first, second, third = pcall(fn)
    if not ok then return nil end
    if type(first) == 'number' then
        if first == -30000 or type(second) ~= 'number' or type(third) ~= 'number' then return nil end
        return {x=first, y=second, z=third}
    end
    return position(first)
end

local function key(pos)
    return pos.x .. ',' .. pos.y .. ',' .. pos.z
end

local function emit(value)
    print(json.encode(value, {pretty=false}))
end

local function failure(code, message)
    emit({schemaVersion=SCHEMA_VERSION, error={code=code, message=message}})
end

local function find_unit(unit_id)
    local unit = select(1, safe(function() return df.unit.find(unit_id) end))
    if unit == nil then return nil end
    return unit
end

local function validate_bounds(center, radius)
    local bounds = {
        x1=center.x-radius,
        y1=center.y-radius,
        z=center.z,
        x2=center.x+radius,
        y2=center.y+radius,
        width=(radius*2)+1,
        height=(radius*2)+1
    }

    for y=bounds.y1,bounds.y2 do
        for x=bounds.x1,bounds.x2 do
            local pos = {x=x, y=y, z=bounds.z}
            if not dfhack.maps.isValidTilePos(pos)
               or dfhack.maps.getTileType(pos) == nil
               or dfhack.maps.getTileBlock(pos) == nil then
                return nil, 'Requested surroundings contain unavailable map tiles.'
            end
        end
    end

    return bounds, nil
end

local function terrain_class(attrs, has_building)
    if has_building then return 'building' end
    if attrs == nil then return nil end

    local shape = enum_name(df.tiletype_shape, attrs.shape)
    if shape == 'WALL' or shape == 'FORTIFICATION' then return 'wall' end
    if shape == 'RAMP' or shape == 'RAMP_TOP' then return 'ramp' end
    if shape == 'FLOOR' or shape == 'BOULDER' or shape == 'BROOK_TOP' then return 'floor' end
    if shape ~= nil and string.find(shape, 'STAIR', 1, true) then return 'floor' end

    local shape_attrs = df.tiletype_shape.attrs[attrs.shape]
    local walkable = shape_attrs and walkable_flag(shape_attrs.walkable) or nil
    if walkable == true then return 'floor' end

    return nil
end

local function index_unit_counts(bounds, warnings)
    local counts = {}
    local units = dfhack.units.getUnitsInBox(bounds.x1, bounds.y1, bounds.z, bounds.x2, bounds.y2, bounds.z) or {}

    for unit_index, unit in ipairs(units) do
        if unit_index > MAX_UNITS_SCANNED then
            table.insert(warnings, 'Unit scan stopped at the configured limit; visible unit counts may be incomplete.')
            break
        end

        local pos = read_position(function() return dfhack.units.getPosition(unit) end)
        if pos ~= nil and pos.z == bounds.z and pos.x >= bounds.x1 and pos.x <= bounds.x2 and pos.y >= bounds.y1 and pos.y <= bounds.y2 then
            local cell_key = key(pos)
            counts[cell_key] = (counts[cell_key] or 0) + 1
        end
    end

    return counts
end

local function sorted_legend(values)
    local result = {}
    for value in pairs(values) do table.insert(result, value) end
    table.sort(result)
    return result
end

local function main(...)
    local args = {...}
    if not dfhack.isMapLoaded() then
        return failure('NO_MAP_LOADED', 'DFHack is reachable, but no fortress map is loaded.')
    end

    local unit_id, unit_err = integer(args[1], 'unitId', 0, 2147483647)
    if unit_err then return failure('INVALID_ARGUMENT', unit_err) end

    local radius = DEFAULT_RADIUS
    if args[2] ~= nil then
        local parsed_radius, radius_err = integer(args[2], 'radius', DEFAULT_RADIUS, MAX_RADIUS)
        if radius_err then return failure('INVALID_ARGUMENT', radius_err) end
        radius = parsed_radius
    end

    local unit = find_unit(unit_id)
    if unit == nil then
        return failure('INVALID_ARGUMENT', 'No unit exists with the requested unitId.')
    end

    local center = read_position(function() return dfhack.units.getPosition(unit) end)
    if center == nil then
        return failure('INVALID_ARGUMENT', 'The requested unit has no valid map position.')
    end

    local bounds, bounds_err = validate_bounds(center, radius)
    if bounds_err then return failure('INVALID_BOUNDS', bounds_err) end

    local warnings = {}
    local legend_values = {}
    local unit_counts = index_unit_counts(bounds, warnings)
    local cells = {}

    for dy=-radius,radius do
        for dx=-radius,radius do
            local pos = {x=center.x+dx, y=center.y+dy, z=center.z}
            local tiletype = dfhack.maps.getTileType(pos)
            local attrs = tiletype and df.tiletype.attrs[tiletype] or nil
            local flags = select(1, dfhack.maps.getTileFlags(pos))
            local is_hidden = flags ~= nil and flags.hidden == true
            local cell = {dx=dx, dy=dy, visibility=is_hidden and 'hidden' or 'visible'}

            if not is_hidden then
                local building = dfhack.buildings.findAtTile(pos)
                local zones = dfhack.buildings.findCivzonesAt(pos) or {}
                local has_building = building ~= nil or #zones > 0
                local cell_key = key(pos)
                local terrain = terrain_class(attrs, has_building)
                local shape_attrs = attrs and df.tiletype_shape.attrs[attrs.shape] or nil
                local walkable = shape_attrs and walkable_flag(shape_attrs.walkable) or nil

                if terrain ~= nil then
                    cell.terrainClass = terrain
                    legend_values[terrain] = true
                end

                if walkable ~= nil then
                    cell.walkable = walkable
                end

                if has_building then
                    cell.featureClass = 'building'
                    legend_values['building'] = true
                end

                local unit_count = unit_counts[cell_key] or 0
                if unit_count > 0 then
                    cell.unitCount = unit_count
                end
            end

            table.insert(cells, cell)
        end
    end

    emit({
        schemaVersion=SCHEMA_VERSION,
        provenance={kind='live-dfhack', generatedBy='fortress-souls/get-dwarf-surroundings'},
        gameTime={year=dfhack.world.ReadCurrentYear(), tick=dfhack.world.ReadCurrentTick()},
        bounds={radius=radius, width=bounds.width, height=bounds.height},
        cells=cells,
        legend=sorted_legend(legend_values),
        warnings=warnings
    })
end

local script_args = {...}
local _, err = safe(function() main(table.unpack(script_args)) end)
if err then failure('SCRIPT_FAILED', err) end

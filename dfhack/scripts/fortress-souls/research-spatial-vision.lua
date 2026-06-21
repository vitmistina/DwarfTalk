local json = require('json')

local SCHEMA_VERSION = 'fortress-souls-spatial-vision-research.v0.1'
local MAX_WIDTH = 25
local MAX_HEIGHT = 25
local MAX_RADIUS = 12
local MAX_ITEMS_SCANNED = 200000
local MAX_ITEMS_PER_CELL = 40
local MAX_FLOWS_PER_CELL = 20
local MAX_UNITS_SCANNED = 10000
local MAX_BLOCK_FLOWS_SCANNED = 5000
local MAX_BLOCK_EVENTS_SCANNED = 2048
local MAX_ZONES_PER_CELL = 20

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

local function same_position(a, b)
    return a ~= nil and b ~= nil and a.x == b.x and a.y == b.y and a.z == b.z
end

local function key(pos)
    return pos.x .. ',' .. pos.y .. ',' .. pos.z
end

local function append(index, pos, value, limit)
    local cell_key = key(pos)
    local values = index[cell_key]
    if values == nil then
        values = {}
        index[cell_key] = values
    end
    if #values < limit then table.insert(values, value) end
end

local function material_json(matinfo)
    if matinfo == nil then return nil end
    return {
        token=scalar(select(1, safe(function() return matinfo:getToken() end))),
        type=scalar(select(1, safe(function() return matinfo.type end))),
        index=scalar(select(1, safe(function() return matinfo.index end)))
    }
end

local function find_unit(unit_id)
    local unit = select(1, safe(function() return df.unit.find(unit_id) end))
    if unit == nil then return nil end
    return unit
end

local function parse_query(args)
    if args[1] == 'unit' then
        local unit_id, err = integer(args[2], 'unitId', 0, 2147483647)
        if err then return nil, err end
        local radius, radius_err = integer(args[3], 'radius', 0, MAX_RADIUS)
        if radius_err then return nil, radius_err end
        local z_offset, z_err = integer(args[4], 'zOffset', -20, 20)
        if z_err then return nil, z_err end
        local unit = find_unit(unit_id)
        if unit == nil then return nil, 'No unit exists with the requested unitId.' end
        local unit_pos = read_position(function() return dfhack.units.getPosition(unit) end)
        if unit_pos == nil then return nil, 'The requested unit has no valid map position.' end
        return {
            mode='unit', unitId=unit_id, radius=radius, zOffset=z_offset,
            x=unit_pos.x-radius, y=unit_pos.y-radius, z=unit_pos.z+z_offset,
            width=radius*2+1, height=radius*2+1, unitPosition=unit_pos
        }, nil
    end

    if args[1] == 'area' then
        local x, x_err = integer(args[2], 'x', 0, 1000000)
        if x_err then return nil, x_err end
        local y, y_err = integer(args[3], 'y', 0, 1000000)
        if y_err then return nil, y_err end
        local z, z_err = integer(args[4], 'z', 0, 10000)
        if z_err then return nil, z_err end
        local width, width_err = integer(args[5], 'width', 1, MAX_WIDTH)
        if width_err then return nil, width_err end
        local height, height_err = integer(args[6], 'height', 1, MAX_HEIGHT)
        if height_err then return nil, height_err end
        return {mode='area', x=x, y=y, z=z, width=width, height=height}, nil
    end

    return nil, 'Expected: unit <unitId> <radius> <zOffset> or area <x> <y> <z> <width> <height>.'
end

local function validate_bounds(query)
    local map_x, map_y, map_z = dfhack.maps.getTileSize()
    if query.x < 0 or query.y < 0 or query.z < 0 or
       query.x + query.width > map_x or query.y + query.height > map_y or query.z >= map_z then
        return nil, 'Requested rectangle is outside the loaded map.'
    end
    local last = {x=query.x+query.width-1, y=query.y+query.height-1, z=query.z}
    for y=query.y,query.y+query.height-1 do
        for x=query.x,query.x+query.width-1 do
            local pos = {x=x, y=y, z=query.z}
            if not dfhack.maps.isValidTilePos(pos) or dfhack.maps.getTileType(pos) == nil or dfhack.maps.getTileBlock(pos) == nil then
                return nil, 'Requested rectangle contains unavailable map tiles.'
            end
        end
    end
    return {x1=query.x, y1=query.y, z=query.z, x2=last.x, y2=last.y, width=query.width, height=query.height}, nil
end

local function in_bounds(pos, bounds)
    return pos ~= nil and pos.z == bounds.z and pos.x >= bounds.x1 and pos.x <= bounds.x2 and pos.y >= bounds.y1 and pos.y <= bounds.y2
end

local function index_items(bounds, warnings)
    local index = {}
    local scanned = 0
    for _, item in ipairs(df.global.world.items.other.IN_PLAY) do
        if scanned >= MAX_ITEMS_SCANNED then
            table.insert(warnings, 'Item scan stopped at the configured limit; cell item lists may be incomplete.')
            break
        end
        scanned = scanned + 1
        local pos = read_position(function() return dfhack.items.getPosition(item) end)
        if in_bounds(pos, bounds) then
            local container = select(1, safe(function() return dfhack.items.getContainer(item) end))
            append(index, pos, {
                id=scalar(item.id),
                description=scalar(select(1, safe(function() return dfhack.items.getReadableDescription(item) end))),
                type=enum_name(df.item_type, select(1, safe(function() return item:getType() end))),
                stackSize=scalar(select(1, safe(function() return item:getStackSize() end))),
                contained=container ~= nil,
                containerId=container and scalar(container.id) or nil
            }, MAX_ITEMS_PER_CELL)
        end
    end
    return index, scanned
end

local function index_units(bounds, warnings)
    local index = {}
    local units = dfhack.units.getUnitsInBox(bounds.x1, bounds.y1, bounds.z, bounds.x2, bounds.y2, bounds.z) or {}
    for unit_index, unit in ipairs(units) do
        if unit_index > MAX_UNITS_SCANNED then
            table.insert(warnings, 'Unit scan stopped at the configured limit; cell unit lists may be incomplete.')
            break
        end
        local pos = read_position(function() return dfhack.units.getPosition(unit) end)
        if in_bounds(pos, bounds) then
            append(index, pos, {
                id=scalar(unit.id),
                name=scalar(select(1, safe(function() return dfhack.units.getReadableName(unit) end))),
                invader=scalar(select(1, safe(function() return dfhack.units.isInvader(unit) end))),
                danger=scalar(select(1, safe(function() return dfhack.units.isDanger(unit) end))),
                greatDanger=scalar(select(1, safe(function() return dfhack.units.isGreatDanger(unit) end)))
            }, 20)
        end
    end
    return index
end

local function index_flows(bounds, warnings)
    local index = {}
    local visited = {}
    for y=bounds.y1,bounds.y2 do
        for x=bounds.x1,bounds.x2 do
            local pos = {x=x, y=y, z=bounds.z}
            local block = dfhack.maps.getTileBlock(pos)
            if block ~= nil then
                local block_key = tostring(math.floor(x/16)) .. ',' .. tostring(math.floor(y/16)) .. ',' .. bounds.z
                if not visited[block_key] then
                    visited[block_key] = true
                    for flow_index, flow in ipairs(block.flows or {}) do
                        if flow_index > MAX_BLOCK_FLOWS_SCANNED then
                            table.insert(warnings, 'A map-block flow scan stopped at the configured limit; cell flow lists may be incomplete.')
                            break
                        end
                        local flow_pos = position(flow.pos)
                        if in_bounds(flow_pos, bounds) then
                            append(index, flow_pos, {
                                type=enum_name(df.flow_type, scalar(flow.type)),
                                density=scalar(flow.density),
                                material=material_json(select(1, safe(function() return dfhack.matinfo.decode(flow.mat_type, flow.mat_index) end)))
                            }, MAX_FLOWS_PER_CELL)
                        end
                    end
                end
            end
        end
    end
    return index
end

local function mineral_material(block, pos)
    if block == nil then return nil end
    local matches = {}
    for event_index, event in ipairs(block.block_events or {}) do
        if event_index > MAX_BLOCK_EVENTS_SCANNED then break end
        if select(1, safe(function() return event:getType() end)) == df.block_square_event_type.mineral and
           select(1, safe(function() return dfhack.maps.getTileAssignment(event.tile_bitmask, pos.x, pos.y) end)) then
            table.insert(matches, event)
        end
    end
    if #matches == 0 then return nil end
    local event = matches[#matches]
    local material = material_json(select(1, safe(function() return dfhack.matinfo.decode(0, event.inorganic_mat) end)))
    if material ~= nil then material.source = 'mineralBlockEvent' end
    return material
end

local function layer_material(block, pos)
    if block == nil then return nil end
    local material = select(1, safe(function()
        local region = dfhack.maps.getRegionBiome(dfhack.maps.getTileBiomeRgn(pos))
        local biome = region and df.world_geo_biome.find(region.geo_index) or nil
        local layer_index = block.designation[pos.x % 16][pos.y % 16].geolayer_index
        local layer = biome and biome.layers[layer_index] or nil
        return layer and dfhack.matinfo.decode(0, layer.mat_index) or nil
    end))
    local result = material_json(material)
    if result ~= nil then result.source = 'geologicalLayer' end
    return result
end

local function terrain_symbol(shape)
    if shape == 'WALL' or shape == 'FORTIFICATION' then return '#' end
    if shape == 'FLOOR' then return '.' end
    if shape == 'RAMP' or shape == 'RAMP_TOP' then return '^' end
    if shape and string.find(shape, 'STAIR', 1, true) then return '>' end
    if shape == 'EMPTY' or shape == 'BROOK_TOP' then return ' ' end
    return '?'
end

local function choose_symbol(cell)
    if cell.hidden then return '?' end
    for _, unit in ipairs(cell.units) do if unit.greatDanger then return 'X' end end
    for _, unit in ipairs(cell.units) do if unit.danger then return '!' end end
    for _, unit in ipairs(cell.units) do if unit.invader then return 'I' end end
    if #cell.units > 0 then return 'u' end
    if cell.liquid.type == 'MAGMA' and cell.liquid.depth > 0 then return 'M' end
    if cell.liquid.type == 'WATER' and cell.liquid.depth > 0 then return '~' end
    for _, flow in ipairs(cell.flows) do if flow.type == 'Fire' or flow.type == 'Dragonfire' then return 'F' end end
    if cell.building ~= nil then return 'B' end
    if cell.construction ~= nil then return 'C' end
    if #cell.items > 0 then return '*' end
    if cell.plant ~= nil then return 'p' end
    if #cell.flows > 0 then return 'f' end
    return terrain_symbol(cell.terrain.shape)
end

local LEGEND = {
    ['?']='hidden or unclassified tile', X='unit classified as great danger', ['!']='unit classified as danger',
    I='invader unit', u='other unit', M='magma', ['~']='water', F='fire flow', B='building or zone',
    C='construction', ['*']='item', p='plant', f='non-fire flow', ['#']='wall or fortification',
    ['.']='floor', ['^']='ramp', ['>']='stair', [' ']='open space'
}

local function summarize_cell(pos, item_index, unit_index, flow_index)
    local tiletype = dfhack.maps.getTileType(pos)
    local attrs = tiletype and df.tiletype.attrs[tiletype] or nil
    local flags = select(1, dfhack.maps.getTileFlags(pos))
    local block = dfhack.maps.getTileBlock(pos)
    local plant = dfhack.maps.getPlantAtTile(pos)
    local building = dfhack.buildings.findAtTile(pos)
    local zones = dfhack.buildings.findCivzonesAt(pos) or {}
    local construction = dfhack.constructions.findAtTile(pos)
    local shape = attrs and enum_name(df.tiletype_shape, attrs.shape) or nil
    local shape_attrs = attrs and df.tiletype_shape.attrs[attrs.shape] or nil
    local liquid_depth = flags and scalar(flags.flow_size) or 0
    local liquid_type = nil
    if liquid_depth and liquid_depth > 0 then liquid_type = flags.liquid_type and 'MAGMA' or 'WATER' end
    local geological = mineral_material(block, pos) or layer_material(block, pos)
    local construction_material = construction and material_json(select(1, safe(function() return dfhack.matinfo.decode(construction) end))) or nil
    local cell = {
        x=pos.x, y=pos.y, z=pos.z,
        hidden=flags ~= nil and flags.hidden == true,
        visible=scalar(select(1, safe(function() return dfhack.maps.isTileVisible(pos) end))),
        terrain={
            tiletype=enum_name(df.tiletype, tiletype), shape=shape,
            material=attrs and enum_name(df.tiletype_material, attrs.material) or nil,
            special=attrs and enum_name(df.tiletype_special, attrs.special) or nil,
            variant=attrs and enum_name(df.tiletype_variant, attrs.variant) or nil,
            direction=attrs and scalar(attrs.direction) or nil
        },
        walkable=shape_attrs and scalar(shape_attrs.walkable) or nil,
        liquid={type=liquid_type, depth=liquid_depth or 0},
        geologicalMaterial=geological or {status='unknown', reason='No verified mineral block event resolved this tile.'},
        construction=construction and {id=scalar(construction.id), material=construction_material} or nil,
        plant=plant and {id=scalar(plant.id), material=scalar(plant.material)} or nil,
        building=building and {id=scalar(building.id), type=scalar(select(1, safe(function() return building:getType() end)))} or nil,
        zones={}, units=unit_index[key(pos)] or {}, items=item_index[key(pos)] or {}, flows=flow_index[key(pos)] or {}
    }
    for zone_index, zone in ipairs(zones) do
        if zone_index > MAX_ZONES_PER_CELL then break end
        table.insert(cell.zones, {id=scalar(zone.id), type=scalar(select(1, safe(function() return zone:getType() end)))})
    end
    if cell.building == nil and #cell.zones > 0 then cell.building = {zoneOnly=true} end
    cell.symbol = choose_symbol(cell)
    return cell
end

local function emit(value)
    print(json.encode(value, {pretty=false}))
end

local function failure(code, message, query)
    emit({schemaVersion=SCHEMA_VERSION, error={code=code, message=message}, query=query})
end

local function main(...)
    local args = {...}
    if not dfhack.isMapLoaded() then return failure('NO_MAP_LOADED', 'DFHack is reachable, but no fortress map is loaded.') end
    local query, query_err = parse_query(args)
    if query_err then return failure('INVALID_ARGUMENT', query_err) end
    local bounds, bounds_err = validate_bounds(query)
    if bounds_err then return failure('INVALID_BOUNDS', bounds_err, query) end
    local warnings = {}
    local item_index, items_scanned = index_items(bounds, warnings)
    local unit_index = index_units(bounds, warnings)
    local flow_index = index_flows(bounds, warnings)
    local cells, grid, symbols = {}, {}, {}
    for y=bounds.y1,bounds.y2 do
        local row = {}
        for x=bounds.x1,bounds.x2 do
            local cell = summarize_cell({x=x,y=y,z=bounds.z}, item_index, unit_index, flow_index)
            table.insert(cells, cell)
            table.insert(row, cell.symbol)
            symbols[cell.symbol] = true
        end
        table.insert(grid, table.concat(row))
    end
    local legend = {}
    for symbol, description in pairs(LEGEND) do if symbols[symbol] then table.insert(legend, {symbol=symbol, meaning=description}) end end
    table.sort(legend, function(a,b) return a.symbol < b.symbol end)
    emit({
        schemaVersion=SCHEMA_VERSION,
        provenance={kind='live-dfhack', generatedBy='fortress-souls/research-spatial-vision'},
        environment={dfVersion=dfhack.getDFVersion(),dfhackVersion=dfhack.getDFHackVersion(),dfhackGitDescription=dfhack.getGitDescription()},
        query=query, bounds=bounds,
        limits={maxWidth=MAX_WIDTH,maxHeight=MAX_HEIGHT,maxRadius=MAX_RADIUS,maxItemsScanned=MAX_ITEMS_SCANNED,maxItemsPerCell=MAX_ITEMS_PER_CELL,maxFlowsPerCell=MAX_FLOWS_PER_CELL,maxUnitsScanned=MAX_UNITS_SCANNED,maxBlockFlowsScanned=MAX_BLOCK_FLOWS_SCANNED,maxBlockEventsScanned=MAX_BLOCK_EVENTS_SCANNED,maxZonesPerCell=MAX_ZONES_PER_CELL},
        gameTime={year=dfhack.world.ReadCurrentYear(),tick=dfhack.world.ReadCurrentTick()},
        scan={itemsScanned=items_scanned}, cells=cells, grid=grid, legend=legend,
        symbolPrecedence={'hidden','great danger','danger','invader','unit','magma','water','fire','building or zone','construction','item','plant','other flow','terrain'},
        unsupported={
            'Geological material remains unknown when neither a mineral block event nor the tile geological layer resolves safely.',
            'Rendered tiles are intentionally not a semantic source.',
            'Item and flow lists are truncated at documented limits.'
        },
        warnings=warnings
    })
end

local script_args = {...}
local _, err = safe(function() main(table.unpack(script_args)) end)
if err then failure('SCRIPT_FAILED', err) end

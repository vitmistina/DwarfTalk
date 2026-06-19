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

local function maybe_errors(errors)
    if errors ~= nil and next(errors) then
        return errors
    end

    return nil
end

local function add_error(errors, key, err)
    if err ~= nil then
        errors[key] = tostring(err)
    end
end

local function read_path(root, path)
    local current = root

    for _, part in ipairs(path) do
        if current == nil then
            return nil, nil
        end

        local value, err = safe(function()
            return current[part]
        end)

        if err ~= nil then
            return nil, err
        end

        current = value
    end

    return current, nil
end

local function optional_path(root, path)
    local value, _ = read_path(root, path)
    return json_scalar(value)
end

local function vector_count(value)
    if value == nil then
        return 0, nil
    end

    local count, err = safe(function()
        return #value
    end)

    if err ~= nil then
        return nil, err
    end

    return count, nil
end

local function position_to_json(pos)
    if pos == nil then
        return nil
    end

    return {
        x = optional_path(pos, {"x"}),
        y = optional_path(pos, {"y"}),
        z = optional_path(pos, {"z"})
    }
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

local function summarize_identity(unit)
    local errors = {}

    local readable_name, readable_name_err = safe(function()
        return dfhack.units.getReadableName(unit)
    end)

    local profession_name, profession_name_err = safe(function()
        return dfhack.units.getProfessionName(unit)
    end)

    local profession_id, profession_id_err = safe(function()
        return dfhack.units.getProfession(unit)
    end)

    local age, age_err = safe(function()
        return dfhack.units.getAge(unit, true)
    end)

    local apparent_age, apparent_age_err = safe(function()
        return dfhack.units.getAge(unit, false)
    end)

    local creature_id, creature_id_err = safe(function()
        local race = unit.race
        local creature = df.global.world.raws.creatures.all[race]
        return creature.creature_id
    end)

    local caste_id, caste_id_err = safe(function()
        local race = unit.race
        local caste = unit.caste
        local creature = df.global.world.raws.creatures.all[race]
        return creature.caste[caste].caste_id
    end)

    add_error(errors, "readableName", readable_name_err)
    add_error(errors, "professionName", profession_name_err)
    add_error(errors, "professionId", profession_id_err)
    add_error(errors, "age", age_err)
    add_error(errors, "apparentAge", apparent_age_err)
    add_error(errors, "creatureId", creature_id_err)
    add_error(errors, "casteId", caste_id_err)

    return {
        id = tostring(unit.id),
        rawId = json_scalar(unit.id),
        histFigureId = optional_path(unit, {"hist_figure_id"}),
        race = optional_path(unit, {"race"}),
        caste = optional_path(unit, {"caste"}),
        sex = optional_path(unit, {"sex"}),
        creatureId = json_scalar(creature_id),
        casteId = json_scalar(caste_id),
        readableName = json_scalar(readable_name),
        professionName = json_scalar(profession_name),
        professionId = json_scalar(profession_id),
        professionToken = enum_name(df.profession, profession_id),
        age = json_scalar(age),
        apparentAge = json_scalar(apparent_age),
        birthYear = optional_path(unit, {"birth_year"}),
        birthTime = optional_path(unit, {"birth_time"}),
        civId = optional_path(unit, {"civ_id"}),
        populationId = optional_path(unit, {"population_id"}),
        culturalIdentity = optional_path(unit, {"cultural_identity"}),
        errors = maybe_errors(errors)
    }
end

local function summarize_flags(unit)
    local errors = {}

    local is_citizen, is_citizen_err = safe(function()
        return dfhack.units.isCitizen(unit, true)
    end)

    local is_resident, is_resident_err = safe(function()
        return dfhack.units.isResident(unit, true)
    end)

    local is_sane, is_sane_err = safe(function()
        return dfhack.units.isSane(unit)
    end)

    local is_alive, is_alive_err = safe(function()
        return dfhack.units.isAlive(unit)
    end)

    local is_dwarf, is_dwarf_err = safe(function()
        return dfhack.units.isDwarf(unit)
    end)

    local is_active, is_active_err = safe(function()
        return dfhack.units.isActive(unit)
    end)

    add_error(errors, "isCitizen", is_citizen_err)
    add_error(errors, "isResident", is_resident_err)
    add_error(errors, "isSane", is_sane_err)
    add_error(errors, "isAlive", is_alive_err)
    add_error(errors, "isDwarf", is_dwarf_err)
    add_error(errors, "isActive", is_active_err)

    return {
        isCitizen = json_scalar(is_citizen),
        isResident = json_scalar(is_resident),
        isSane = json_scalar(is_sane),
        isAlive = json_scalar(is_alive),
        isDwarf = json_scalar(is_dwarf),
        isActive = json_scalar(is_active),

        flags1 = {
            dead = optional_path(unit, {"flags1", "dead"}),
            inactive = optional_path(unit, {"flags1", "inactive"}),
            caged = optional_path(unit, {"flags1", "caged"}),
            tame = optional_path(unit, {"flags1", "tame"}),
            chained = optional_path(unit, {"flags1", "chained"}),
            merchant = optional_path(unit, {"flags1", "merchant"}),
            diplomat = optional_path(unit, {"flags1", "diplomat"}),
            forest = optional_path(unit, {"flags1", "forest"}),
            onGround = optional_path(unit, {"flags1", "on_ground"}),
            projectile = optional_path(unit, {"flags1", "projectile"}),
            activeInvader = optional_path(unit, {"flags1", "active_invader"}),
            hiddenInAmbush = optional_path(unit, {"flags1", "hidden_in_ambush"})
        },

        flags2 = {
            resident = optional_path(unit, {"flags2", "resident"}),
            visitor = optional_path(unit, {"flags2", "visitor"}),
            calculatedNerves = optional_path(unit, {"flags2", "calculated_nerves"}),
            calculatedBodyparts = optional_path(unit, {"flags2", "calculated_bodyparts"})
        },

        flags3 = {
            ghostly = optional_path(unit, {"flags3", "ghostly"}),
            scout = optional_path(unit, {"flags3", "scout"}),
            visitorUninvited = optional_path(unit, {"flags3", "visitor_uninvited"}),
            visitor = optional_path(unit, {"flags3", "visitor"})
        },

        errors = maybe_errors(errors)
    }
end

local function summarize_position(unit)
    local pos = optional_path(unit, {"pos"})
    local path_dest = optional_path(unit, {"path", "dest"})

    return {
        pos = position_to_json(pos),
        pathDestination = position_to_json(path_dest),
        burrowId = optional_path(unit, {"burrow_id"}),
        idleArea = position_to_json(optional_path(unit, {"idle_area"})),
        idleAreaType = optional_path(unit, {"idle_area_type"})
    }
end

local function summarize_current_job(unit)
    local errors = {}

    local job, job_err = safe(function()
        if unit.job then
            return unit.job.current_job
        end

        return nil
    end)

    add_error(errors, "currentJob", job_err)

    if job == nil then
        return {
            present = false,
            errors = maybe_errors(errors)
        }
    end

    local job_type, job_type_err = safe(function()
        return job.job_type
    end)

    local item_count, item_count_err = vector_count(optional_path(job, {"items"}))

    add_error(errors, "jobType", job_type_err)
    add_error(errors, "itemCount", item_count_err)

    return {
        present = true,
        id = optional_path(job, {"id"}),
        type = json_scalar(job_type),
        token = enum_name(df.job_type, job_type),
        pos = position_to_json(optional_path(job, {"pos"})),
        completionTimer = optional_path(job, {"completion_timer"}),
        flags = {
            repeatJob = optional_path(job, {"flags", "repeat"}),
            suspend = optional_path(job, {"flags", "suspend"}),
            working = optional_path(job, {"flags", "working"}),
            special = optional_path(job, {"flags", "special"}),
            fetchInput = optional_path(job, {"flags", "fetch_input"})
        },
        itemCount = json_scalar(item_count),
        errors = maybe_errors(errors)
    }
end

local function summarize_counters(unit)
    return {
        pain = optional_path(unit, {"counters", "pain"}),
        nausea = optional_path(unit, {"counters", "nausea"}),
        winded = optional_path(unit, {"counters", "winded"}),
        stunned = optional_path(unit, {"counters", "stunned"}),
        unconscious = optional_path(unit, {"counters", "unconscious"}),
        dizziness = optional_path(unit, {"counters", "dizziness"}),
        suffocation = optional_path(unit, {"counters", "suffocation"}),
        webbed = optional_path(unit, {"counters", "webbed"}),
        paralysis = optional_path(unit, {"counters", "paralysis"}),
        numbness = optional_path(unit, {"counters", "numbness"}),
        fever = optional_path(unit, {"counters", "fever"}),
        exhaustion = optional_path(unit, {"counters", "exhaustion"}),

        hungerTimer = optional_path(unit, {"counters2", "hunger_timer"}),
        thirstTimer = optional_path(unit, {"counters2", "thirst_timer"}),
        sleepinessTimer = optional_path(unit, {"counters2", "sleepiness_timer"}),

        bloodCount = optional_path(unit, {"body", "blood_count"}),
        infectionLevel = optional_path(unit, {"body", "infection_level"}),

        movementSpeed = json_scalar(select(1, safe(function()
            return dfhack.units.computeMovementSpeed(unit)
        end))),

        slowdownFactor = json_scalar(select(1, safe(function()
            return dfhack.units.computeSlowdownFactor(unit)
        end)))
    }
end

local function summarize_body(unit)
    local errors = {}

    local body = optional_path(unit, {"body"})

    if body == nil then
        return {
            present = false
        }
    end

    local wounds = optional_path(unit, {"body", "wounds"})
    local wound_count, wound_count_err = vector_count(wounds)

    local components = optional_path(unit, {"body", "components"})
    local body_part_status = optional_path(components, {"body_part_status"})
    local body_part_count, body_part_count_err = vector_count(body_part_status)

    add_error(errors, "woundCount", wound_count_err)
    add_error(errors, "bodyPartStatusCount", body_part_count_err)

    local wound_items = {}

    if wounds ~= nil then
        for index, wound in ipairs(wounds) do
            if index > 12 then
                break
            end

            local parts = optional_path(wound, {"parts"})
            local layers = optional_path(wound, {"layer_status"})
            local part_count = nil
            local layer_count = nil

            part_count = select(1, vector_count(parts))
            layer_count = select(1, vector_count(layers))

            table.insert(wound_items, {
                index = json_scalar(index),
                id = optional_path(wound, {"id"}),
                age = optional_path(wound, {"age"}),
                woundType = optional_path(wound, {"type"}),
                partsCount = json_scalar(part_count),
                layerStatusCount = json_scalar(layer_count),
                flags = {
                    active = optional_path(wound, {"flags", "active"}),
                    severedPart = optional_path(wound, {"flags", "severed_part"}),
                    mortalWound = optional_path(wound, {"flags", "mortal_wound"})
                }
            })
        end
    end

    return {
        present = true,
        bloodCount = optional_path(unit, {"body", "blood_count"}),
        infectionLevel = optional_path(unit, {"body", "infection_level"}),
        sizeInfo = {
            sizeCur = optional_path(unit, {"body", "size_info", "size_cur"}),
            sizeBase = optional_path(unit, {"body", "size_info", "size_base"}),
            areaCur = optional_path(unit, {"body", "size_info", "area_cur"}),
            areaBase = optional_path(unit, {"body", "size_info", "area_base"})
        },
        wounds = {
            count = json_scalar(wound_count),
            items = wound_items
        },
        bodyPartStatusCount = json_scalar(body_part_count),
        errors = maybe_errors(errors)
    }
end

local function summarize_inventory_item(inv_item, index)
    local errors = {}

    local item, item_err = safe(function()
        return inv_item.item
    end)

    local mode, mode_err = safe(function()
        return inv_item.mode
    end)

    local body_part_id, body_part_id_err = safe(function()
        return inv_item.body_part_id
    end)

    add_error(errors, "item", item_err)
    add_error(errors, "mode", mode_err)
    add_error(errors, "bodyPartId", body_part_id_err)

    if item == nil then
        return {
            index = json_scalar(index),
            itemPresent = false,
            mode = json_scalar(mode),
            modeToken = enum_name(df.unit_inventory_item.T_mode, mode),
            bodyPartId = json_scalar(body_part_id),
            errors = maybe_errors(errors)
        }
    end

    local description, description_err = safe(function()
        return dfhack.items.getDescription(item, 0, true)
    end)

    local item_type, item_type_err = safe(function()
        return item:getType()
    end)

    add_error(errors, "description", description_err)
    add_error(errors, "itemType", item_type_err)

    return {
        index = json_scalar(index),
        itemPresent = true,
        mode = json_scalar(mode),
        modeToken = enum_name(df.unit_inventory_item.T_mode, mode),
        bodyPartId = json_scalar(body_part_id),
        item = {
            id = optional_path(item, {"id"}),
            type = json_scalar(item_type),
            typeToken = enum_name(df.item_type, item_type),
            subtype = optional_path(item, {"subtype"}),
            matType = optional_path(item, {"mat_type"}),
            matIndex = optional_path(item, {"mat_index"}),
            description = json_scalar(description),
            pos = position_to_json(optional_path(item, {"pos"})),
            flags = {
                onGround = optional_path(item, {"flags", "on_ground"}),
                inInventory = optional_path(item, {"flags", "in_inventory"}),
                owned = optional_path(item, {"flags", "owned"}),
                forbidden = optional_path(item, {"flags", "forbid"}),
                dump = optional_path(item, {"flags", "dump"}),
                garbageCollect = optional_path(item, {"flags", "garbage_collect"})
            }
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_inventory(unit)
    local errors = {}

    local inventory, inventory_err = safe(function()
        return unit.inventory
    end)

    add_error(errors, "inventory", inventory_err)

    local count, count_err = vector_count(inventory)
    add_error(errors, "count", count_err)

    local items = {}

    if inventory ~= nil then
        for index, inv_item in ipairs(inventory) do
            if index > 30 then
                break
            end

            local item_summary, item_summary_err = safe(function()
                return summarize_inventory_item(inv_item, index)
            end)

            if item_summary_err ~= nil then
                table.insert(items, {
                    index = json_scalar(index),
                    error = tostring(item_summary_err)
                })
            else
                table.insert(items, item_summary)
            end
        end
    end

    return {
        count = json_scalar(count),
        includedCount = #items,
        items = items,
        notes = {
            limit = 30,
            description = "dfhack.items.getDescription when available"
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_noble_positions(unit)
    local errors = {}

    local positions, positions_err = safe(function()
        return dfhack.units.getNoblePositions(unit)
    end)

    add_error(errors, "getNoblePositions", positions_err)

    local items = {}

    if positions ~= nil then
        for index, pos in ipairs(positions) do
            local entity_id = optional_path(pos, {"entity", "id"})
            local assignment_id = optional_path(pos, {"assignment", "id"})
            local position_code = optional_path(pos, {"position", "code"})

            local name_singular = nil
            local name_plural = nil

            local singular, singular_err = safe(function()
                if pos.position and pos.position.name then
                    return pos.position.name[0]
                end

                return nil
            end)

            local plural, plural_err = safe(function()
                if pos.position and pos.position.name_plural then
                    return pos.position.name_plural[0]
                end

                return nil
            end)

            if singular_err == nil then
                name_singular = singular
            end

            if plural_err == nil then
                name_plural = plural
            end

            table.insert(items, {
                index = json_scalar(index),
                entityId = json_scalar(entity_id),
                assignmentId = json_scalar(assignment_id),
                positionCode = json_scalar(position_code),
                name = json_scalar(name_singular),
                pluralName = json_scalar(name_plural)
            })
        end
    end

    return {
        count = #items,
        items = items,
        errors = maybe_errors(errors)
    }
end

local function summarize_military(unit)
    return {
        squadId = optional_path(unit, {"military", "squad_id"}),
        squadPosition = optional_path(unit, {"military", "squad_position"}),
        patrolCooldown = optional_path(unit, {"military", "patrol_cooldown"}),
        curSquadOrderId = optional_path(unit, {"military", "cur_squad_order_id"}),
        pickupFlags = {
            update = optional_path(unit, {"military", "pickup_flags", "update"}),
            individualChoice = optional_path(unit, {"military", "pickup_flags", "individual_choice"})
        }
    }
end

local function summarize_relationship_ids(unit)
    local relation_ids = optional_path(unit, {"relationship_ids"})
    local items = {}

    if relation_ids ~= nil then
        for index, relation_id in ipairs(relation_ids) do
            if relation_id ~= nil and relation_id ~= -1 then
                table.insert(items, {
                    index = json_scalar(index),
                    token = enum_name(df.unit_relationship_type, index),
                    unitId = json_scalar(relation_id)
                })
            end
        end
    end

    return {
        count = #items,
        items = items,
        notes = {
            source = "unit.relationship_ids static array when available"
        }
    }
end

local function summarize_ref(ref, index)
    local ref_type, ref_type_err = safe(function()
        return ref:getType()
    end)

    local item_id = optional_path(ref, {"item_id"})
    local unit_id = optional_path(ref, {"unit_id"})
    local histfig_id = optional_path(ref, {"histfig_id"})
    local entity_id = optional_path(ref, {"entity_id"})
    local building_id = optional_path(ref, {"building_id"})
    local activity_id = optional_path(ref, {"activity_id"})
    local abstract_building_id = optional_path(ref, {"abstract_building_id"})

    return {
        index = json_scalar(index),
        type = json_scalar(ref_type),
        typeToken = enum_name(df.general_ref_type, ref_type),
        itemId = item_id,
        unitId = unit_id,
        histfigId = histfig_id,
        entityId = entity_id,
        buildingId = building_id,
        activityId = activity_id,
        abstractBuildingId = abstract_building_id,
        refTypeName = json_scalar(optional_path(ref, {"_type"})),
        errors = ref_type_err ~= nil and { getType = tostring(ref_type_err) } or nil
    }
end

local function summarize_refs(unit)
    local errors = {}

    local general_refs, general_refs_err = safe(function()
        return unit.general_refs
    end)

    local specific_refs, specific_refs_err = safe(function()
        return unit.specific_refs
    end)

    add_error(errors, "generalRefs", general_refs_err)
    add_error(errors, "specificRefs", specific_refs_err)

    local general_count = select(1, vector_count(general_refs))
    local specific_count = select(1, vector_count(specific_refs))

    local general_items = {}

    if general_refs ~= nil then
        for index, ref in ipairs(general_refs) do
            if index > 30 then
                break
            end

            local ref_summary, ref_summary_err = safe(function()
                return summarize_ref(ref, index)
            end)

            if ref_summary_err ~= nil then
                table.insert(general_items, {
                    index = json_scalar(index),
                    error = tostring(ref_summary_err)
                })
            else
                table.insert(general_items, ref_summary)
            end
        end
    end

    local specific_items = {}

    if specific_refs ~= nil then
        for index, ref in ipairs(specific_refs) do
            if index > 30 then
                break
            end

            table.insert(specific_items, {
                index = json_scalar(index),
                refTypeName = json_scalar(optional_path(ref, {"_type"})),
                type = optional_path(ref, {"type"}),
                data = json_scalar(optional_path(ref, {"data"}))
            })
        end
    end

    return {
        general = {
            count = json_scalar(general_count),
            includedCount = #general_items,
            items = general_items
        },
        specific = {
            count = json_scalar(specific_count),
            includedCount = #specific_items,
            items = specific_items
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_syndromes(unit)
    local errors = {}

    local syndromes, syndromes_err = safe(function()
        return unit.syndromes.active
    end)

    add_error(errors, "activeSyndromes", syndromes_err)

    local count = select(1, vector_count(syndromes))
    local items = {}

    if syndromes ~= nil then
        for index, syndrome in ipairs(syndromes) do
            if index > 20 then
                break
            end

            table.insert(items, {
                index = json_scalar(index),
                type = optional_path(syndrome, {"type"}),
                year = optional_path(syndrome, {"year"}),
                yearTick = optional_path(syndrome, {"year_time"}),
                ticks = optional_path(syndrome, {"ticks"}),
                syndromeId = optional_path(syndrome, {"syndrome_id"}),
                flags = {
                    isSick = optional_path(syndrome, {"flags", "is_sick"}),
                    isHidden = optional_path(syndrome, {"flags", "is_hidden"})
                }
            })
        end
    end

    return {
        count = json_scalar(count),
        includedCount = #items,
        items = items,
        errors = maybe_errors(errors)
    }
end

local function build_prompt_candidates(snapshot)
    local inventory_items = {}

    for _, inv in ipairs(snapshot.inventory.items or {}) do
        if inv.item ~= nil and inv.item.description ~= nil then
            table.insert(inventory_items, {
                modeToken = inv.modeToken,
                description = inv.item.description,
                typeToken = inv.item.typeToken
            })
        end
    end

    local notable_counters = {}

    for key, value in pairs(snapshot.counters or {}) do
        if type(value) == "number" and value > 0 then
            table.insert(notable_counters, {
                token = key,
                value = value
            })
        end
    end

    table.sort(notable_counters, function(a, b)
        if a.value ~= b.value then
            return a.value > b.value
        end

        return tostring(a.token) < tostring(b.token)
    end)

    return {
        currentJob = snapshot.job,
        position = snapshot.position,
        inventoryItems = inventory_items,
        notableCounters = notable_counters,
        wounds = snapshot.body ~= nil and snapshot.body.wounds or nil,
        noblePositions = snapshot.noblePositions,
        military = snapshot.military,
        relationshipIds = snapshot.relationshipIds,
        notes = {
            inventoryItems = "Item descriptions are good prompt material if present.",
            counters = "Nonzero pain/nausea/exhaustion/etc can ground tone.",
            wounds = "Currently only wound counts and shallow wound fields. Needs deeper health mapping later."
        }
    }
end

local function build_snapshot(unit_id)
    local result = {
        schemaVersion = "fortress-souls-probe-dwarf-live.v0.1",
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

    result.identity = summarize_identity(unit)
    result.flags = summarize_flags(unit)
    result.position = summarize_position(unit)
    result.job = summarize_current_job(unit)
    result.counters = summarize_counters(unit)
    result.body = summarize_body(unit)
    result.inventory = summarize_inventory(unit)
    result.noblePositions = summarize_noble_positions(unit)
    result.military = summarize_military(unit)
    result.relationshipIds = summarize_relationship_ids(unit)
    result.refs = summarize_refs(unit)
    result.syndromes = summarize_syndromes(unit)
    result.promptCandidates = build_prompt_candidates(result)

    return result
end

local function emit(obj)
    local encoded, encode_err = safe(function()
        return json.encode(obj, { pretty = false })
    end)

    if encode_err ~= nil then
        local fallback = {
            schemaVersion = "fortress-souls-probe-dwarf-live.v0.1",
            error = {
                code = "JSON_ENCODE_FAILED",
                message = tostring(encode_err)
            }
        }

        print(json.encode(fallback, { pretty = false }))
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
            schemaVersion = "fortress-souls-probe-dwarf-live.v0.1",
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

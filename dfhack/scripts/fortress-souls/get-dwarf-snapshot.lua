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

local function abs_number(value)
    if value == nil then
        return nil
    end

    if value < 0 then
        return -value
    end

    return value
end

local function pos_to_json(pos)
    if pos == nil then
        return nil
    end

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

local function shallow_copy_array(items, limit)
    local result = {}

    if items == nil then
        return result
    end

    for index, item in ipairs(items) do
        if limit ~= nil and index > limit then
            break
        end

        table.insert(result, item)
    end

    return result
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
        local creature = df.global.world.raws.creatures.all[unit.race]
        return creature.creature_id
    end)

    local caste_id, caste_id_err = safe(function()
        local creature = df.global.world.raws.creatures.all[unit.race]
        return creature.caste[unit.caste].caste_id
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
        histFigureId = json_scalar(unit.hist_figure_id),
        readableName = json_scalar(readable_name),
        professionName = json_scalar(profession_name),
        professionId = json_scalar(profession_id),
        professionToken = enum_name(df.profession, profession_id),
        race = json_scalar(unit.race),
        caste = json_scalar(unit.caste),
        sex = json_scalar(unit.sex),
        creatureId = json_scalar(creature_id),
        casteId = json_scalar(caste_id),
        age = json_scalar(age),
        apparentAge = json_scalar(apparent_age),
        birthYear = json_scalar(unit.birth_year),
        birthTime = json_scalar(unit.birth_time),
        civId = json_scalar(unit.civ_id),
        populationId = json_scalar(unit.population_id),
        culturalIdentity = json_scalar(unit.cultural_identity),
        errors = maybe_errors(errors)
    }
end

local function summarize_flags(unit)
    return {
        isCitizen = json_scalar(select(1, safe(function()
            return dfhack.units.isCitizen(unit, true)
        end))),
        isResident = json_scalar(select(1, safe(function()
            return dfhack.units.isResident(unit, true)
        end))),
        isSane = json_scalar(select(1, safe(function()
            return dfhack.units.isSane(unit)
        end))),
        isAlive = json_scalar(select(1, safe(function()
            return dfhack.units.isAlive(unit)
        end))),
        isDwarf = json_scalar(select(1, safe(function()
            return dfhack.units.isDwarf(unit)
        end))),
        isActive = json_scalar(select(1, safe(function()
            return dfhack.units.isActive(unit)
        end))),
        flags1 = {
            dead = json_scalar(select(1, read_path(unit, {"flags1", "dead"}))),
            inactive = json_scalar(select(1, read_path(unit, {"flags1", "inactive"}))),
            caged = json_scalar(select(1, read_path(unit, {"flags1", "caged"}))),
            chained = json_scalar(select(1, read_path(unit, {"flags1", "chained"}))),
            onGround = json_scalar(select(1, read_path(unit, {"flags1", "on_ground"})))
        },
        flags2 = {
            resident = json_scalar(select(1, read_path(unit, {"flags2", "resident"}))),
            visitor = json_scalar(select(1, read_path(unit, {"flags2", "visitor"})))
        },
        flags3 = {
            ghostly = json_scalar(select(1, read_path(unit, {"flags3", "ghostly"}))),
            visitor = json_scalar(select(1, read_path(unit, {"flags3", "visitor"})))
        }
    }
end

local function summarize_location(unit)
    local pos = select(1, read_field(unit, "pos"))
    local path_dest = select(1, read_path(unit, {"path", "dest"}))
    local idle_area = select(1, read_field(unit, "idle_area"))
    local idle_area_type = select(1, read_field(unit, "idle_area_type"))

    return {
        position = pos_to_json(pos),
        pathDestination = pos_to_json(path_dest),
        idleArea = pos_to_json(idle_area),
        idleAreaType = json_scalar(idle_area_type)
    }
end

local function summarize_work(unit)
    local errors = {}

    local job, job_err = read_path(unit, {"job", "current_job"})
    add_error(errors, "currentJob", job_err)

    if job == nil then
        return {
            currentJob = nil,
            errors = maybe_errors(errors)
        }
    end

    local job_type, job_type_err = read_field(job, "job_type")
    local job_id, job_id_err = read_field(job, "id")
    local job_pos, job_pos_err = read_field(job, "pos")
    local completion_timer, completion_timer_err = read_field(job, "completion_timer")
    local items = select(1, read_field(job, "items"))
    local item_count = select(1, vector_count(items))

    add_error(errors, "jobType", job_type_err)
    add_error(errors, "jobId", job_id_err)
    add_error(errors, "jobPos", job_pos_err)
    add_error(errors, "completionTimer", completion_timer_err)

    return {
        currentJob = {
            id = json_scalar(job_id),
            type = json_scalar(job_type),
            token = enum_name(df.job_type, job_type),
            position = pos_to_json(job_pos),
            completionTimer = json_scalar(completion_timer),
            itemCount = json_scalar(item_count)
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_stress(unit)
    local errors = {}

    local raw_stress, raw_stress_err = safe(function()
        if unit.status and unit.status.current_soul and unit.status.current_soul.personality then
            return unit.status.current_soul.personality.stress
        end

        return nil
    end)

    local longterm_stress, longterm_stress_err = safe(function()
        if unit.status and unit.status.current_soul and unit.status.current_soul.personality then
            return unit.status.current_soul.personality.longterm_stress
        end

        return nil
    end)

    local category, category_err = safe(function()
        return dfhack.units.getStressCategory(unit)
    end)

    add_error(errors, "rawStress", raw_stress_err)
    add_error(errors, "longtermStress", longterm_stress_err)
    add_error(errors, "stressCategory", category_err)

    return {
        raw = json_scalar(raw_stress),
        longterm = json_scalar(longterm_stress),
        category = json_scalar(category),
        categoryScale = "0-most-stressed-6-least-stressed",
        errors = maybe_errors(errors)
    }
end

local function summarize_health(unit)
    local wounds = select(1, read_path(unit, {"body", "wounds"}))
    local wound_count = select(1, vector_count(wounds))
    local wound_items = {}

    if wounds ~= nil then
        for index, wound in ipairs(wounds) do
            if index > 8 then
                break
            end

            local parts = select(1, read_field(wound, "parts"))
            local layers = select(1, read_field(wound, "layer_status"))

            table.insert(wound_items, {
                index = json_scalar(index),
                id = json_scalar(select(1, read_field(wound, "id"))),
                age = json_scalar(select(1, read_field(wound, "age"))),
                partsCount = json_scalar(select(1, vector_count(parts))),
                layerStatusCount = json_scalar(select(1, vector_count(layers)))
            })
        end
    end

    return {
        counters = {
            pain = json_scalar(select(1, read_path(unit, {"counters", "pain"}))),
            nausea = json_scalar(select(1, read_path(unit, {"counters", "nausea"}))),
            winded = json_scalar(select(1, read_path(unit, {"counters", "winded"}))),
            stunned = json_scalar(select(1, read_path(unit, {"counters", "stunned"}))),
            unconscious = json_scalar(select(1, read_path(unit, {"counters", "unconscious"}))),
            dizziness = json_scalar(select(1, read_path(unit, {"counters", "dizziness"}))),
            suffocation = json_scalar(select(1, read_path(unit, {"counters", "suffocation"}))),
            paralysis = json_scalar(select(1, read_path(unit, {"counters", "paralysis"}))),
            numbness = json_scalar(select(1, read_path(unit, {"counters", "numbness"}))),
            fever = json_scalar(select(1, read_path(unit, {"counters", "fever"}))),
            exhaustion = json_scalar(select(1, read_path(unit, {"counters", "exhaustion"}))),
            hungerTimer = json_scalar(select(1, read_path(unit, {"counters2", "hunger_timer"}))),
            thirstTimer = json_scalar(select(1, read_path(unit, {"counters2", "thirst_timer"}))),
            sleepinessTimer = json_scalar(select(1, read_path(unit, {"counters2", "sleepiness_timer"})))
        },
        body = {
            bloodCount = json_scalar(select(1, read_path(unit, {"body", "blood_count"}))),
            infectionLevel = json_scalar(select(1, read_path(unit, {"body", "infection_level"}))),
            wounds = {
                count = json_scalar(wound_count),
                items = wound_items
            }
        },
        movement = {
            speed = json_scalar(select(1, safe(function()
                return dfhack.units.computeMovementSpeed(unit)
            end))),
            slowdownFactor = json_scalar(select(1, safe(function()
                return dfhack.units.computeSlowdownFactor(unit)
            end)))
        }
    }
end

local function summarize_inventory_item(inv_item, index)
    local errors = {}

    local item, item_err = read_field(inv_item, "item")
    local mode, mode_err = read_field(inv_item, "mode")
    local body_part_id, body_part_id_err = read_field(inv_item, "body_part_id")

    add_error(errors, "item", item_err)
    add_error(errors, "mode", mode_err)
    add_error(errors, "bodyPartId", body_part_id_err)

    if item == nil then
        return {
            index = json_scalar(index),
            itemPresent = false,
            mode = json_scalar(mode),
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
        mode = json_scalar(mode),
        bodyPartId = json_scalar(body_part_id),
        item = {
            id = json_scalar(item.id),
            type = json_scalar(item_type),
            typeToken = enum_name(df.item_type, item_type),
            subtype = json_scalar(item.subtype),
            matType = json_scalar(item.mat_type),
            matIndex = json_scalar(item.mat_index),
            description = json_scalar(description)
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_inventory(unit)
    local errors = {}
    local inventory, inventory_err = read_field(unit, "inventory")

    add_error(errors, "inventory", inventory_err)

    local count = select(1, vector_count(inventory))
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
        errors = maybe_errors(errors)
    }
end

local function summarize_skills(unit, soul)
    local errors = {}
    local skills = {}

    local skill_list, skill_list_err = read_field(soul, "skills")
    add_error(errors, "skills", skill_list_err)

    if skill_list ~= nil then
        for _, skill in ipairs(skill_list) do
            local skill_id = select(1, read_field(skill, "id"))
            local rating = select(1, read_field(skill, "rating"))
            local rust = select(1, read_field(skill, "rusty"))

            local nominal = select(1, safe(function()
                return dfhack.units.getNominalSkill(unit, skill_id)
            end))

            local effective = select(1, safe(function()
                return dfhack.units.getEffectiveSkill(unit, skill_id)
            end))

            local experience = select(1, safe(function()
                return dfhack.units.getExperience(unit, skill_id)
            end))

            local total_experience = select(1, safe(function()
                return dfhack.units.getExperience(unit, skill_id, true)
            end))

            table.insert(skills, {
                id = json_scalar(skill_id),
                token = enum_name(df.job_skill, skill_id),
                rating = json_scalar(rating),
                nominal = json_scalar(nominal),
                effective = json_scalar(effective),
                experience = json_scalar(experience),
                totalExperience = json_scalar(total_experience),
                rust = json_scalar(rust)
            })
        end
    end

    return {
        count = #skills,
        items = skills,
        errors = maybe_errors(errors)
    }
end

local function summarize_traits(personality)
    local errors = {}
    local traits = {}

    local trait_list, trait_list_err = read_field(personality, "traits")
    add_error(errors, "traits", trait_list_err)

    if trait_list ~= nil then
        for index, trait_value in ipairs(trait_list) do
            local raw_value = json_scalar(trait_value)
            local deviation = nil

            if raw_value ~= nil then
                deviation = raw_value - 50
            end

            table.insert(traits, {
                index = json_scalar(index),
                token = enum_name(df.personality_facet_type, index),
                value = raw_value,
                deviationFromNeutral50 = json_scalar(deviation),
                absDeviationFromNeutral50 = json_scalar(abs_number(deviation))
            })
        end
    end

    return {
        count = #traits,
        items = traits,
        notes = {
            neutralApproximation = 50,
            indexSource = "unit_personality.traits static-array indexed by personality_facet_type"
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_values(personality)
    local errors = {}
    local values = {}

    local value_list, value_list_err = read_field(personality, "values")
    add_error(errors, "values", value_list_err)

    if value_list ~= nil then
        for index, belief in ipairs(value_list) do
            local belief_type = select(1, read_field(belief, "type"))
            local belief_strength = select(1, read_field(belief, "strength"))

            table.insert(values, {
                index = json_scalar(index),
                type = json_scalar(belief_type),
                token = enum_name(df.value_type, belief_type),
                strength = json_scalar(belief_strength)
            })
        end
    end

    return {
        count = #values,
        items = values,
        notes = {
            sourceStruct = "personality_valuest",
            fields = "type, strength"
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_needs(personality)
    local errors = {}
    local needs = {}

    local need_list, need_list_err = read_field(personality, "needs")
    add_error(errors, "needs", need_list_err)

    if need_list ~= nil then
        for index, need in ipairs(need_list) do
            local need_id = select(1, read_field(need, "id"))
            local focus_level = select(1, read_field(need, "focus_level"))
            local need_level = select(1, read_field(need, "need_level"))
            local deity_id = select(1, read_field(need, "deity_id"))

            local is_unmet = nil
            local is_deeply_unmet = nil

            if focus_level ~= nil then
                is_unmet = focus_level < 0
                is_deeply_unmet = focus_level < -999
            end

            table.insert(needs, {
                index = json_scalar(index),
                id = json_scalar(need_id),
                token = enum_name(df.need_type, need_id),
                focusLevel = json_scalar(focus_level),
                needLevel = json_scalar(need_level),
                deityId = json_scalar(deity_id),
                isUnmet = json_scalar(is_unmet),
                isDeeplyUnmet = json_scalar(is_deeply_unmet)
            })
        end
    end

    return {
        count = #needs,
        items = needs,
        notes = {
            focusLevel = "goes toward 400 when satisfied; below 0 means unmet pressure",
            deeplyUnmetThreshold = "focusLevel below -999 is represented by personality flag has_unmet_needs",
            needLevel = "raw severity/decay pressure, not current satisfaction"
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_mannerisms(personality)
    local errors = {}
    local mannerisms = {}

    local mannerism_list, mannerism_list_err = read_field(personality, "mannerism")
    add_error(errors, "mannerism", mannerism_list_err)

    if mannerism_list ~= nil then
        for index, mannerism in ipairs(mannerism_list) do
            local mannerism_type = select(1, read_field(mannerism, "type"))
            local situation_type = select(1, read_field(mannerism, "situation"))

            table.insert(mannerisms, {
                index = json_scalar(index),
                type = json_scalar(mannerism_type),
                token = enum_name(df.mannerism_type, mannerism_type),
                situation = json_scalar(situation_type),
                situationToken = enum_name(df.mannerism_situation_type, situation_type)
            })
        end
    end

    return {
        count = #mannerisms,
        items = mannerisms,
        errors = maybe_errors(errors)
    }
end

local function summarize_preferences(personality)
    local errors = {}
    local preferences = {}

    local preference_list, preference_list_err = read_field(personality, "preferences")
    add_error(errors, "preferences", preference_list_err)

    if preference_list ~= nil then
        for index, preference in ipairs(preference_list) do
            local preference_type = select(1, read_field(preference, "type"))

            table.insert(preferences, {
                index = json_scalar(index),
                type = json_scalar(preference_type),
                token = enum_name(df.personality_preference_type, preference_type),
                var1 = json_scalar(select(1, read_field(preference, "var1"))),
                var2 = json_scalar(select(1, read_field(preference, "var2"))),
                var3 = json_scalar(select(1, read_field(preference, "var3"))),
                var4 = json_scalar(select(1, read_field(preference, "var4"))),
                seed = json_scalar(select(1, read_field(preference, "seed")))
            })
        end
    end

    return {
        count = #preferences,
        items = preferences,
        notes = {
            warning = "Raw preference var fields are not resolved to material/creature/item names in v0.1."
        },
        errors = maybe_errors(errors)
    }
end

local function summarize_personality(personality)
    if personality == nil then
        return {
            present = false
        }
    end

    return {
        present = true,
        state = {
            stress = json_scalar(select(1, read_field(personality, "stress"))),
            longtermStress = json_scalar(select(1, read_field(personality, "longterm_stress"))),
            currentFocus = json_scalar(select(1, read_field(personality, "current_focus"))),
            undistractedFocus = json_scalar(select(1, read_field(personality, "undistracted_focus"))),
            timeWithoutDistress = json_scalar(select(1, read_field(personality, "time_without_distress"))),
            timeWithoutEustress = json_scalar(select(1, read_field(personality, "time_without_eustress"))),
            likesOutdoors = json_scalar(select(1, read_field(personality, "likes_outdoors"))),
            combatHardened = json_scalar(select(1, read_field(personality, "combat_hardened"))),
            outdoorDislikeCounter = json_scalar(select(1, read_field(personality, "outdoor_dislike_counter"))),
            temptation = {
                greed = json_scalar(select(1, read_field(personality, "temptation_greed"))),
                lust = json_scalar(select(1, read_field(personality, "temptation_lust"))),
                power = json_scalar(select(1, read_field(personality, "temptation_power"))),
                anger = json_scalar(select(1, read_field(personality, "temptation_anger")))
            }
        },
        traits = summarize_traits(personality),
        values = summarize_values(personality),
        needs = summarize_needs(personality),
        mannerisms = summarize_mannerisms(personality),
        preferences = summarize_preferences(personality)
    }
end

local function summarize_roles(unit)
    local positions = select(1, safe(function()
        return dfhack.units.getNoblePositions(unit)
    end))

    local nobles = {}

    if positions ~= nil then
        for index, pos in ipairs(positions) do
            table.insert(nobles, {
                index = json_scalar(index),
                entityId = json_scalar(select(1, read_path(pos, {"entity", "id"}))),
                assignmentId = json_scalar(select(1, read_path(pos, {"assignment", "id"}))),
                positionCode = json_scalar(select(1, read_path(pos, {"position", "code"}))),
                name = json_scalar(select(1, safe(function()
                    if pos.position and pos.position.name then
                        return pos.position.name[0]
                    end

                    return nil
                end)))
            })
        end
    end

    return {
        noblePositions = {
            count = #nobles,
            items = nobles
        },
        military = {
            squadId = json_scalar(select(1, read_path(unit, {"military", "squad_id"}))),
            squadPosition = json_scalar(select(1, read_path(unit, {"military", "squad_position"}))),
            patrolCooldown = json_scalar(select(1, read_path(unit, {"military", "patrol_cooldown"}))),
            curSquadOrderId = json_scalar(select(1, read_path(unit, {"military", "cur_squad_order_id"})))
        }
    }
end

local function summarize_relationship_ids(unit)
    local relation_ids = select(1, read_field(unit, "relationship_ids"))
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
            warning = "Only raw relationship ids. Names and relationship semantics not resolved in v0.1."
        }
    }
end

local function build_top_skills(skills_result, limit)
    local items = shallow_copy_array(skills_result.items)

    table.sort(items, function(a, b)
        local a_rating = a.effective or a.rating or 0
        local b_rating = b.effective or b.rating or 0

        if a_rating ~= b_rating then
            return a_rating > b_rating
        end

        return (a.totalExperience or 0) > (b.totalExperience or 0)
    end)

    local result = {}

    for _, skill in ipairs(items) do
        if (skill.effective or skill.rating or 0) > 0 then
            table.insert(result, skill)
        end

        if #result >= limit then
            break
        end
    end

    return result
end

local function build_extreme_traits(traits_result, limit)
    local items = {}

    for _, trait in ipairs(traits_result.items or {}) do
        if trait.value ~= nil and trait.absDeviationFromNeutral50 ~= nil and trait.absDeviationFromNeutral50 >= 10 then
            table.insert(items, {
                token = trait.token,
                value = trait.value,
                deviationFromNeutral50 = trait.deviationFromNeutral50,
                absDeviationFromNeutral50 = trait.absDeviationFromNeutral50,
                polarity = trait.deviationFromNeutral50 >= 0 and "high" or "low"
            })
        end
    end

    table.sort(items, function(a, b)
        if a.absDeviationFromNeutral50 ~= b.absDeviationFromNeutral50 then
            return a.absDeviationFromNeutral50 > b.absDeviationFromNeutral50
        end

        return tostring(a.token) < tostring(b.token)
    end)

    return shallow_copy_array(items, limit)
end

local function build_strong_values(values_result, limit)
    local items = {}

    for _, value in ipairs(values_result.items or {}) do
        if value.strength ~= nil then
            table.insert(items, {
                token = value.token,
                type = value.type,
                strength = value.strength,
                absStrength = abs_number(value.strength),
                polarity = value.strength >= 0 and "positive" or "negative"
            })
        end
    end

    table.sort(items, function(a, b)
        if a.absStrength ~= b.absStrength then
            return a.absStrength > b.absStrength
        end

        return tostring(a.token) < tostring(b.token)
    end)

    return shallow_copy_array(items, limit)
end

local function build_strong_needs(needs_result, limit)
    local items = {}

    for _, need in ipairs(needs_result.items or {}) do
        local include = false

        if need.needLevel ~= nil and need.needLevel >= 2 then
            include = true
        end

        if need.focusLevel ~= nil and need.focusLevel < 0 then
            include = true
        end

        if include then
            table.insert(items, {
                token = need.token,
                id = need.id,
                focusLevel = need.focusLevel,
                needLevel = need.needLevel,
                deityId = need.deityId,
                isUnmet = need.isUnmet,
                isDeeplyUnmet = need.isDeeplyUnmet
            })
        end
    end

    table.sort(items, function(a, b)
        local a_unmet = a.focusLevel ~= nil and a.focusLevel < 0
        local b_unmet = b.focusLevel ~= nil and b.focusLevel < 0

        if a_unmet ~= b_unmet then
            return a_unmet
        end

        if (a.needLevel or 0) ~= (b.needLevel or 0) then
            return (a.needLevel or 0) > (b.needLevel or 0)
        end

        return tostring(a.token) < tostring(b.token)
    end)

    return shallow_copy_array(items, limit)
end

local function build_inventory_candidates(inventory_result, limit)
    local items = {}

    for _, inv in ipairs(inventory_result.items or {}) do
        if inv.item ~= nil and inv.item.description ~= nil then
            table.insert(items, {
                description = inv.item.description,
                typeToken = inv.item.typeToken,
                bodyPartId = inv.bodyPartId,
                mode = inv.mode
            })
        end
    end

    return shallow_copy_array(items, limit)
end

local function build_notable_counters(health)
    local counters = {}
    local source = health.counters or {}

    for key, value in pairs(source) do
        if type(value) == "number" and value > 0 then
            table.insert(counters, {
                token = key,
                value = value
            })
        end
    end

    table.sort(counters, function(a, b)
        if a.value ~= b.value then
            return a.value > b.value
        end

        return tostring(a.token) < tostring(b.token)
    end)

    return counters
end

local function build_prompt_candidates(snapshot)
    return {
        identity = {
            displayName = snapshot.identity.readableName,
            professionName = snapshot.identity.professionName,
            creatureId = snapshot.identity.creatureId,
            casteId = snapshot.identity.casteId
        },
        currentJob = snapshot.work.currentJob,
        location = snapshot.location,
        topSkills = build_top_skills(snapshot.skills, 10),
        extremeTraits = build_extreme_traits(snapshot.personality.traits or { items = {} }, 14),
        strongValues = build_strong_values(snapshot.personality.values or { items = {} }, 10),
        strongNeeds = build_strong_needs(snapshot.personality.needs or { items = {} }, 10),
        mannerisms = shallow_copy_array((snapshot.personality.mannerisms or { items = {} }).items or {}, 8),
        inventoryItems = build_inventory_candidates(snapshot.inventory, 12),
        notableCounters = build_notable_counters(snapshot.health),
        wounds = snapshot.health.body ~= nil and snapshot.health.body.wounds or nil,
        noblePositions = snapshot.roles.noblePositions,
        military = snapshot.roles.military,
        relationships = snapshot.relationships,
        notes = {
            traits = "Only extreme traits are prompt candidates. Full raw trait list remains in personality.traits.",
            values = "Value strength sign is raw and should be interpreted cautiously.",
            needs = "Need focusLevel is current satisfaction/pressure; needLevel is raw decay/severity pressure.",
            inventory = "Inventory descriptions are useful grounding but usually low priority unless item is unusual.",
            health = "Nonzero counters and wounds are useful grounding. Zero counters should not be emphasized."
        }
    }
end

local function build_snapshot(unit_id)
    local result = {
        schemaVersion = "fortress-souls-dwarf-snapshot.v0.1",
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

    local soul = nil

    if unit.status ~= nil then
        soul = unit.status.current_soul
    end

    result.identity = summarize_identity(unit)
    result.flags = summarize_flags(unit)
    result.location = summarize_location(unit)
    result.work = summarize_work(unit)
    result.stress = summarize_stress(unit)
    result.health = summarize_health(unit)
    result.inventory = summarize_inventory(unit)
    result.roles = summarize_roles(unit)
    result.relationships = summarize_relationship_ids(unit)
    result.soulPresent = soul ~= nil

    if soul == nil then
        result.error = {
            code = "NO_CURRENT_SOUL",
            message = "Unit has no current soul; skills/personality cannot be read."
        }

        return result
    end

    local personality = select(1, read_field(soul, "personality"))

    result.skills = summarize_skills(unit, soul)
    result.personality = summarize_personality(personality)
    result.promptCandidates = build_prompt_candidates(result)

    return result
end

local function emit(obj)
    local encoded, encode_err = safe(function()
        return json.encode(obj, { pretty = false })
    end)

    if encode_err ~= nil then
        print(json.encode({
            schemaVersion = "fortress-souls-dwarf-snapshot.v0.1",
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
            schemaVersion = "fortress-souls-dwarf-snapshot.v0.1",
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

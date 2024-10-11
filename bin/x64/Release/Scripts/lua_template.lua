
--**************************getReperesInMission***********************************
--getReperesInMission
--**************************getReperesInMission***********************************
function getReperesInMission()
 
	reperesInMission = {}
	allUnitId = {}
	allGroupId = {}
	allName = {}
	maxiId = 0
	idRepereUpdate = {}
	idRepereDelete = {}
	cancelVar = false
	debugTxt = ""

	if not ASTI_ref then ASTI_ref = {} end
	if not templateGroupId_refId then templateGroupId_refId = {} end

	for sideName, side in pairs(mission.coalition) do    
		for countryN, country in pairs(side.country) do
			for category, groups in pairs(country) do
				if (category == 'vehicle' or category == 'static' or category == 'ship' or category == 'plane' or category == 'helicopter') and type(groups) == 'table' and groups['group'] then
					for Ngroup, group in pairs(groups['group']) do
						allGroupId[group.groupId] = true
						allName[group.name] = true
						if group.groupId > maxiId then maxiId = group.groupId end

						local templateName = ''
						local adding = false
						local foundAstiInGroupName = false

						if group.hidden == nil then group.hidden = false end
						local heading = 0
						if group.heading  then heading = group.heading end

						local entry = {
							refId = group.groupId,
							x = group.x,
							y = group.y,
							hdg = heading,
							name = group.name,
							templateName = templateName,
							sideName = sideName,
							countryName = country.name,
							countryId = country.id,
							hidden = group.hidden,
							action = 'INSERT',
						}

						if string.find(group.name, 'ASTI') then
							templateName = string.gsub(group.name, 'ASTI' ,'' )
							foundAstiInGroupName = true

							if string.sub(templateName, 1, 1) == '_' or string.sub(templateName, 1, 1) == ' ' then
								templateName = string.sub(templateName, 2)
							end

							templateName = removeLastNumberAfterDash(templateName)

							entry = {
								refId = group.groupId,
								x = group.x,
								y = group.y,
								hdg = heading,
								name = group.name,
								templateName = templateName,
								sideName = sideName,
								countryName = country.name,
								countryId = country.id,
								hidden = group.hidden,
								action = 'INSERT',
							}
						end



						local foundAstiUnit = false
						
						if foundAstiInGroupName or foundAstiUnit then 
							--ajoute les anciennes positions dans le 1er wpt, pour comparer si oui ou non, le campainMaker à bougé l'unité de ref
						   if ASTI_ref[group.name] then

								if ASTI_ref[group.name].x == entry.x and ASTI_ref[group.name].y == entry.y 
								and ASTI_ref[group.name].hdg == entry.hdg and ASTI_ref[group.name].hidden == entry.hidden 
								and ASTI_ref[group.name].sideName == entry.sideName 
								then
									adding = false
								else
									idRepereUpdate[group.groupId] = group.name
									adding = true
									ASTI_ref[group.name].x = entry.x
									ASTI_ref[group.name].y = entry.y
									ASTI_ref[group.name].hdg = entry.hdg
									ASTI_ref[group.name].id = entry.refId
									ASTI_ref[group.name].hidden = entry.hidden
									ASTI_ref[group.name].sideName = entry.sideName
								end
							else
								adding = true
								ASTI_ref[group.name] = {
									x = entry.x,
									y = entry.y,
									hdg = entry.hdg,
									id = entry.refId,
									hidden = entry.hidden,
									sideName = entry.sideName,
								}

							end

							if adding then
								table.insert(reperesInMission, entry)
							end
						end


						for unitN, unit in pairs(group.units) do
							allUnitId[unit.unitId] = true
							if unit.unitId > maxiId then maxiId = unit.unitId end
							allName[unit.name] = true
						end

					end
				end
			end
		end
	end

	-- Table temporaire pour stocker les clés à supprimer
	local toRemove = {}

	-- Première boucle : on parcourt sans supprimer mais on marque les clés à supprimer
	for groupName, group in pairs(ASTI_ref) do
		if not allGroupId[group.id] then
			table.insert(toRemove, groupName)
		end
	end
	-- Deuxième boucle : on supprime les éléments marqués
	for _, groupName in ipairs(toRemove) do
		ASTI_ref[groupName] = nil
	end

	local testTable = ""

	-- Supprime toutes les unités ajoutées si leurs références ont été supprimées
	for sideName, side in pairs(mission.coalition) do 
		for countryN, country in pairs(side.country) do
			for category, groups in pairs(country) do
				if (category == 'vehicle' or category == 'static' or category == 'ship' or category == 'plane' or category == 'helicopter') and type(groups) == 'table' and groups['group'] then
					
					-- Parcours les groupes à l'envers
					for Ngroup = #groups['group'], 1, -1 do

						local group = groups['group'][Ngroup]

						if templateGroupId_refId[group.groupId] then

							local refId = templateGroupId_refId[group.groupId]

							refId = tonumber(refId)

						   -- Si le refId n'est pas dans allGroupId, cela veut dire que la Marqueur à été supprimé
						   --on supprime donc le groupe qui fait parti du template associé au marqueur
							if not allGroupId[refId]  then
								
								idRepereDelete[refId] = true
								for unitN, unit in pairs(group.units) do
									allUnitId[unit.unitId] = nil
								end
								allGroupId[group.groupId] = nil
								-- Supprime le groupe entier
								local testRemove = table.remove(groups['group'], Ngroup)
								if testRemove == nil or testRemove == false then
									print("Error table.remove groupId "..tostring(group.groupId))
								end
								if templateGroupId_refId[group.groupId] then
									templateGroupId_refId[group.groupId] = nil
								end
								
							end

							-- Si le refId est dans idRepereUpdate, on supprime le groupe
							if idRepereUpdate[refId] then

								for unitN, unit in pairs(group.units) do
									allUnitId[unit.unitId] = nil
								end

								allGroupId[group.groupId] = nil
								local testGrouId = group.groupId
								local testGroupName = group.name

								-- Supprime le groupe entier
								local testRemove = table.remove(groups['group'], Ngroup)
								if testRemove == nil or testRemove == false then
									print("Error table.remove groupId "..tostring(group.groupId))
								else
									debugTxt = debugTxt .. " remove() idRepereUpdate "..testGrouId.." "..testGroupName.." /r"
								end

								if templateGroupId_refId[group.groupId] then
									templateGroupId_refId[group.groupId] = nil
								end

							end
						end
					end
				end
			end
		end
	end

	-- local debugGenMFile = io.open("Debug/debugTxt.txt", "w")										
	-- debugGenMFile:write(testTable)																		
	-- debugGenMFile:close()

	-- local camp_str = "reperesInMission = " .. TableSerialization(reperesInMission, 0)						
	-- local campFile = io.open("Debug/reperesInMission.lua", "w")								
	-- campFile:write(camp_str)															
	-- campFile:close()

	reperesInMissionTAB = reperesInMission

	local nbAjout = #reperesInMissionTAB
	local nbUpdate = 0
	local nbDelete = 0

	for n, update in pairs(idRepereUpdate) do
		nbUpdate = nbUpdate + 1
	end
	for n, delete in pairs(idRepereDelete) do
		nbDelete = nbDelete + 1
	end

	nbAjout = nbAjout - nbUpdate
		
	-- Appeler la fonction C# ShowConfirmationDialog
	local shouldContinue = ShowConfirmationDialog(
		'Number of templates to add : ' .. nbAjout..'\n'
		..'Number of templates to update : ' .. nbUpdate..'\n'
		..'Number of templates to delete : ' .. nbDelete..'\n'
		..'\n'
		..'Do you want to continue?')

	-- Si l'utilisateur clique sur Cancel, interrompre le script
	if not shouldContinue then
		cancelVar = true
		-- return cancelVar  -- Interrompre le script Lua
	end

	--traitement de mapResource, pour ajouter ASTI_ref dans le fichier mapResource
	mapResource = addInmapResource(mapResource)

	return reperesInMission
end


--**************************function addTemplateInMission()***********************************
--function addTemplateInMission()
--**************************function addTemplateInMission()***********************************

function addTemplateInMission(marqueurName)
	
	local moduleTxt = ""
	local old_new_unitId = {}

	for T_moduleName, T_value in pairs(staticTemplate.requiredModules) do
		if moduleTxt == "" then moduleTxt = "The template requires these modules: : \n" end
		moduleTxt = moduleTxt .. " "..T_moduleName.."\n"
	end		

	if moduleTxt ~= "" then
		print(moduleTxt)
	end

	for templateN, repere_IM_tmp in pairs(reperesInMissionTAB) do          
		if repere_IM_tmp.name == marqueurName then
			repere_IM = repere_IM_tmp
			break
		end
	end

	--prend en compte le dictionary s'il y en a un (du .stm ou template .miz)
	if dictionary == nil and staticTemplate.localization and staticTemplate.localization.DEFAULT then
		dictionary = staticTemplate.localization.DEFAULT
	end

		----------------------------*****************-----------------------------
	--modifie les noms, ajoute ceux du dictionary s'ils existent
	for sideName, side in pairs(staticTemplate.coalition) do    
		for countryN, country in pairs(side.country) do
			for category, groups in pairs(country) do
				if (category == 'vehicle' or category == 'static' or category == 'ship' or category == 'plane' or category == 'helicopter') and type(groups) == 'table' and groups['group'] then
					for Ngroup, group in pairs(groups['group']) do
						group.hidden = repere_IM.hidden

						if dictionary and dictionary[group.name] then
							local oldName = group.name
							group.name = dictionary[oldName]
						end

						if group.route and group.route.points then				
							for n, wpt in pairs(group.route.points) do
								if wpt.name and dictionary and dictionary[wpt.name] then
									local oldName = wpt.name
									wpt.name = dictionary[oldName]
								end								
							end
						end

						for unitN, unit in pairs(group.units) do
							if dictionary and dictionary[unit.name] then
								local oldName = unit.name
								unit.name = dictionary[oldName]
							end
						end
					end
				end
			end
		end
	end
	
	delta = {
		x = 0,
		y = 0,
		heading = 0,
	}

	min_x = 9999999
	max_x = -9999999
	min_y = 9999999
	max_y = -9999999
	min_hdg = 360
	max_hdg = 0

	local templateReferentiel = {
		x = 0,
		y = 0,
		hdg = 0,
		found = false,
	}

	breakOrder = false
	for sideName, side in pairs(staticTemplate.coalition) do    
		for countryN, country in pairs(side.country) do
			for category, groups in pairs(country) do
				if (category == 'vehicle' or category == 'static' or category == 'ship' or category == 'plane' or category == 'helicopter') and type(groups) == 'table' and groups['group'] then
					for Ngroup, group in pairs(groups['group']) do
						if templateReferentiel.found then break end

						if string.find(group.name, 'REF_') or string.find(group.name, 'REF-') or string.find(group.name, 'REF ') then
							templateReferentiel.x = group.x
							templateReferentiel.y = group.y
							templateReferentiel.hdg = group.units[1].heading
							templateReferentiel.found = true
							-- print("templateReferentiel.hdg: "..tostring(templateReferentiel.hdg).." |name: "..tostring(group.units[1].name))
							break
						end
			
						for unitN, unit in pairs(group.units) do

							if unit.x < min_x then min_x = unit.x end
							if unit.x > max_x then max_x = unit.x end
							if unit.y < min_y then min_y = unit.y end
							if unit.y > max_y then max_y = unit.y end
							if unit.heading < min_hdg then min_hdg = unit.heading end
							if unit.heading > max_hdg then max_hdg = unit.heading end
						end
					end
				end
			end
		end
	end

	if not templateReferentiel.found then
		templateReferentiel.x = (min_x + max_x) / 2
		templateReferentiel.y = (min_y + max_y) / 2
		templateReferentiel.hdg = (min_hdg  + max_hdg ) / 2 
	end

	delta = {
		x = repere_IM.x - templateReferentiel.x,
		y = repere_IM.y - templateReferentiel.y,
		hdg =  repere_IM.hdg - templateReferentiel.hdg,
	}

	-- print("repere_IM.hdg: "..tostring(repere_IM.hdg).." \n "
	-- 		.."templateReferentiel.hdg: "..tostring(templateReferentiel.hdg).." \n "
	-- 		.."delta.hdg: "..tostring(delta.hdg)
	-- 	)



	----------------------------*****************-----------------------------
	-- modifie le template avant de le coller a la mission:
	-- xy heading, ids, names, 
	for sideName, side in pairs(staticTemplate.coalition) do    
		for countryN, country in pairs(side.country) do
			for category, groups in pairs(country) do
				if (category == 'vehicle' or category == 'static' or category == 'ship' or category == 'plane' or category == 'helicopter') and type(groups) == 'table' and groups['group'] then
					for Ngroup, group in pairs(groups['group']) do
						group.hidden = repere_IM.hidden
						
						-- Faire d'abord la rotation du groupe
						group.x, group.y, group.heading = rotateGroupAroundPoint(group, templateReferentiel, delta.hdg)

						-- puis la rotation du plan de vol/route
						if group.route and group.route.points then				
							for n, wpt in pairs(group.route.points) do
								wpt.x, wpt.y, temp = rotateGroupAroundPoint(wpt, templateReferentiel, delta.hdg)							
							end
						end

						-- Déplacer ensuite le groupe selon le delta
						group.x = group.x + delta.x
						group.y = group.y + delta.y
						-- puis déplacement du plan de vol/route
						if group.route and group.route.points then				
							for n, wpt in pairs(group.route.points) do
								wpt.x = wpt.x + delta.x
								wpt.y = wpt.y + delta.y							
							end
						end

						-- Gérer l'ID 
						if allGroupId[group.groupId] then
							group.groupId  = generatGroupId()	
						else
							allGroupId[group.groupId] = true					
						end

						if string.find(group.name, 'ASTI_') then
							group.name = group.name:gsub("ASTI_", "")
						elseif string.find(group.name, 'ASTI') then
							group.name = group.name:gsub("ASTI", "")
						end

						--doublon de nom
						if allName[group.name] then
							group.name = generateNewSingleName(allName, group.name)                         
						end
						allName[group.name] = true

						-- --le wpt 2 defini l orientation du group et de l unite
						-- local distance = 0
						-- if group.route and group.route.points and #group.route.points >= 2 then				
						-- 	distance = GetDistance(group.route.points[1], group.route.points[2])
						-- end


						-- if group.route and group.route.points then				
						-- 	group.route.points[1].x = group.x
						-- 	group.route.points[1].y = group.y

						-- 	for n, wpt in pairs(group.route.points) do
						-- 		if n == 2 then

						-- 			wpt = GetOffsetPoint(group.route.points[1].x, delta.hdg, distance)

						-- 		elseif n >= 3 then
						-- 			wpt.x = wpt.x + delta.x
						-- 			wpt.y = wpt.y + delta.y

						-- 			if wpt.name and dictionary and dictionary[wpt.name] then
						-- 				local oldName = wpt.name
						-- 				wpt.name = dictionary[oldName]
						-- 			end
						-- 		end
						-- 	end


							-- for n = 2, #group.route.points do
							-- 	group.route.points[n].x = group.route.points[n].x + delta.x
							-- 	group.route.points[n].y = group.route.points[n].y + delta.y

							-- 	if group.route.points[n].name and dictionary and dictionary[group.route.points[n].name] then
							-- 		local oldName = group.route.points[n].name
							-- 		group.route.points[n].name = dictionary[oldName]
							-- 	end

							-- end
						-- end
			
						for unitN, unit in pairs(group.units) do
							-- Rotation de l'unité autour du point de référence
							unit.x, unit.y, unit.heading = rotateGroupAroundPoint(unit, templateReferentiel, delta.hdg)
		
							-- Déplacement de l'unité selon le delta
							unit.x = unit.x + delta.x
							unit.y = unit.y + delta.y
		
							if allUnitId[unit.unitId] then
								local oldUnitId = unit.unitId
								unit.unitId = generatUnitId()
								old_new_unitId[oldUnitId] = unit.unitId
							else
								allUnitId[unit.unitId] = true
								old_new_unitId[unit.unitId] = unit.unitId
							end
							
							if string.find(unit.name, 'ASTI_') then
								unit.name = unit.name:gsub("ASTI_", "")
							elseif string.find(unit.name, 'ASTI') then
								unit.name = unit.name:gsub("ASTI", "")
							end

							--doublon de nom
							if allName[unit.name] then
								unit.name = generateNewSingleName(allName, unit.name)
							end
							
							allName[unit.name] = true

						end

						--ajoute la correlation templateGroupId <=> refId
						templateGroupId_refId[group.groupId] = repere_IM.refId
					end
				end
			end
		end
	end


	-- met à jour les linkUnitId (plane on CVN par ex)
	for sideName, side in pairs(staticTemplate.coalition) do    
		for countryN, country in pairs(side.country) do
			for category, groups in pairs(country) do
				if (category == 'vehicle' or category == 'static' or category == 'ship' or category == 'plane' or category == 'helicopter') and type(groups) == 'table' and groups['group'] then
					for Ngroup, group in pairs(groups['group']) do

						if group.route and group.route.points then										
							for n , point in ipairs(group.route.points) do
								if point.linkUnit then
									if old_new_unitId[point.linkUnit] then
										local old_linkUnit = point.linkUnit
										point.linkUnit = old_new_unitId[point.linkUnit]
										
									end
									
								end
							end
						end
					end
				end
			end
		end
	end
	

	local found = {
        country = false,
        category = {},
    }

    local CJTF_data = {
        blue = {
            id = 80,
            name = "CJTF Blue",
        },
        red = {
            id = 81,
            name = "CJTF Red",
        },
    }


	--**************************change tous les pays du Template en CJTF***********************************
	--change tous les pays du Template en CJTF
	--**************************change tous les pays du Template en CJTF***********************************
    if repere_IM.sideName == "blue" then

        for countryN, country in pairs(mission.coalition[repere_IM.sideName].country) do
            if country.name == "CJTF Blue" then
                found.country = true
            end
        end
        if not found.country then
            local entry = {
                id = 80,
                name = "CJTF Blue",
            }
            table.insert(mission.coalition[repere_IM.sideName].country, entry)
        end
    elseif repere_IM.sideName == "red" then
        for countryN, country in pairs(mission.coalition[repere_IM.sideName].country) do
            if country.name == "CJTF Red" then
                found.country = true
            end
            if not found.country then
                local entry = {
                    id = 81,
                    name = "CJTF Red",
                }
                table.insert(mission.coalition[repere_IM.sideName].country, entry)
            end
        end
    end


	--**************************add Template in Mission***********************************
	--add Template in Mission
	--**************************add Template in Mission***********************************

    for T_sideName, T_countries in pairs(staticTemplate.coalition) do  --TEMPLATE
         for T_countryN, T_country in pairs(T_countries.country) do  --TEMPLATE
             for countryN, country_ in pairs(mission.coalition[repere_IM.sideName].country) do 
                if country_.name == CJTF_data[repere_IM.sideName].name then
                    found.country = true
                     for T_category, T_groups in pairs(T_country) do
                        
                        if (T_category == 'vehicle' or T_category == 'static' or T_category == 'ship' or T_category == 'plane' or T_category == 'helicopter') and type(T_groups) == 'table' and T_groups['group'] then
                            if country_[T_category] then  --TEMPLATE
                                found.category[T_category] = true
                                for T_groupN, T_group in pairs(T_groups['group']) do  --TEMPLATE
                                    table.insert(country_[T_category]["group"], T_group)
                                end  
                            else
                                country_[T_category] = {
                                    group = T_groups['group'],
                                }
                            end
                        end
                    end
                end
            end
        end
    end

	
	--**************************requiredModules***********************************
	--ajoute les informations requiredModules
	--**************************requiredModules***********************************
	-- ["requiredModules"] = 
    -- {
    --     ["SAM Sites Asset Pack"] = "SAM Sites Asset Pack",
    --     ["HighDigitSAMs"] = "HighDigitSAMs",
    --     ["CLP_UH60A_MOD"] = "CLP_UH60A_MOD",
    --     ["Massun92-Assetpack"] = "Massun92-Assetpack",
    --     ["VPC Object by voc & virpil.com"] = "VPC Object by voc & virpil.com",
    --     ["Civil_Objects"] = "Civil_Objects",
    -- }, -- end of ["requiredModules"]

	for T_moduleName, T_value in pairs(staticTemplate.requiredModules) do

		mission.requiredModules[T_moduleName] = T_value

	end		
	



	-- s il ne trouve pas le pays, il ajoute tout
	if not found.country then
		for sideName, side in pairs(mission.coalition) do 
			for T_countryN, T_country in pairs(staticTemplate.coalition[sideName].country) do
				table.insert(mission.coalition[sideName].country, T_country)
			end
		end
	end


	-- local debugGenMFile = io.open("debug/debugTxt.txt", "w")										
	-- debugGenMFile:write(debugTxt)																		
	-- debugGenMFile:close()

	-- local camp_str = "testTable_mission = " .. TableSerialization(mission, 0)						
	-- local campFile = io.open("mission.lua", "w")								
	-- campFile:write(camp_str)															
	-- campFile:close()

end
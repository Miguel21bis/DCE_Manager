function pairsByKeys (t, f)
    local a = {}
	local initType
	local dontSort = false
    for n in pairs(t) do initType = type(n) break end
	for n in pairs(t) do 
		table.insert(a, n) 
		if type(n) ~= initType then dontSort = true end
	end
	if not dontSort then 
		table.sort(a, f) 
	end
    local i = 0      -- iterator variable
    local iter = function ()   -- iterator function
        i = i + 1
        if a[i] == nil then return nil
        else return a[i], t[a[i]]
        end
    end
    return iter
end

--function to return distance between two vector2 points
function GetDistance(p1, p2)

	if not p1.x then 
		_affiche(p1, "p1")
	end
	
	if not p2.x then 
		_affiche(p2, "p2")
	end

	local deltax = p2.x - p1.x
	local deltay = p2.y - p1.y
	return math.sqrt(math.pow(deltax, 2) + math.pow(deltay, 2))
end

--function to return a new point offset from an initial point
function GetOffsetPoint(point, heading, distance)
	return {
		x = point.x + math.cos(math.rad(heading)) * distance,
		y = point.y + math.sin(math.rad(heading)) * distance
	}
end

--function to turn a table into a string
function TableSerialization(t, i, params)

	local crlf = ""
	local tab1 = ""
	for n = 1, i do																	--controls the indent for the current text line
		tab1 = tab1 .. "\t"
	end

	local text = "\n"..crlf..tab1.."{\n"..crlf

	local tab = ""
	for n = 1, i + 1 do																	--controls the indent for the current text line
		tab = tab .. "\t"
	end

	-- if params then
	-- 	table.sort(t, function(a,b) return a[params] > b[params]  end)
	-- end
	local stop = false
	for k,v in pairsByKeys(t) do
		if type(k) == "string" then
			k = string.gsub(k, "\n", "\\\n" )
			k = string.gsub(k, "\"", "\\\"" )
			k = string.gsub(k, "'", "\\\'" )
			text = text .. tab .. '["' .. k .. '"] = '
		else
			text = text .. tab .. "[" .. k .. "] = "
		end
		if type(v) == "string" then
			v = string.gsub(v, "\n", "\\\n" )
			v = string.gsub(v, "\"", "\\\"" )
			v = string.gsub(v, "'", "\\\'" )
			text = text .. '"' .. v .. '",\n'..crlf
		elseif type(v) == "number" then
			text = text .. v .. ",\n"..crlf
		elseif type(v) == "table" then
			text = text .. TableSerialization(v, i + 1)
		elseif type(v) == "boolean" then
			if v == true then
				text = text .. "true,\n"..crlf
			else
				text = text .. "false,\n"..crlf
			end
		elseif type(v) == "function" then
			text = text .. v .. ",\n"..crlf
		elseif v == nil then
			text = text .. "nil,\n"..crlf
		end
	end
	tab = ""
	for n = 1, i do																		
		tab = tab .. "\t"
	end
	if i == 0 then
		text = text .. tab .. "}\n"		..crlf											
	else
		text = text .. tab .. "},\n"	..crlf											
	end
	return text
end

function tableToString(t, i)
    if not i then i = 0 end
    local crlf = ""
    local tab1 = string.rep("\t", i)

    local text = "\n"..crlf..tab1.."{\n"..crlf

    local tab = string.rep("\t", i + 1)

    for k, v in pairs(t) do
        -- Gestion des clés (si c'est une chaîne)
        if type(k) == "string" then
			
            k = k:gsub("\\", "\\\\")  -- Doubler les \
            k = k:gsub("\"", "\\\"")   -- Échapper les "
			k = k:gsub("\n", "\\\n" )
            text = text .. tab .. '["' .. k .. '"] = '
        else
            text = text .. tab .. "[" .. k .. "] = "
        end

        -- Gestion des valeurs
        if type(v) == "string" then
			
            -- Préserver les backslashes et tout ce qui suit sans changer leur forme
            v = v:gsub("\\", "\\\\")  -- Préserver les backslashes
            v = v:gsub("\"", "\\\"")  -- Échapper les guillemets
			v = v:gsub("\n", "\\\n" )
            text = text .. '"' .. v .. '",\n'..crlf
        elseif type(v) == "number" then
            text = text .. v .. ",\n"..crlf
        elseif type(v) == "table" then
            text = text .. tableToString(v, i + 1)
        elseif type(v) == "boolean" then
            text = text .. tostring(v) .. ",\n"..crlf
        elseif type(v) == "function" then
            text = text .. "function, \n"..crlf  -- Evite la tentative d'exécuter une fonction
        elseif v == nil then
            text = text .. "nil,\n"..crlf
        end
    end

    tab = string.rep("\t", i)
    if i == 0 then
        text = text .. tab .. "}\n" .. crlf
    else
        text = text .. tab .. "},\n" .. crlf
    end
    return text
end


function tableToStringOLD(t, i)
	if not i then i = 0 end
	local crlf = ""
	local tab1 = ""
	for n = 1, i do																	--controls the indent for the current text line
		tab1 = tab1 .. "\t"
	end

	local text = "\n"..crlf..tab1.."{\n"..crlf

	local tab = ""
	for n = 1, i + 1 do																	--controls the indent for the current text line
		tab = tab .. "\t"
	end

	-- if params then
	-- 	table.sort(t, function(a,b) return a[params] > b[params]  end)
	-- end
	local stop = false
	for k,v in pairsByKeys(t) do
		if type(k) == "string" then
			k = string.gsub(k, "\n", "\\\n" )

			
			k = k:gsub("\\\"", "\\\\\\\"" )
			k = k:gsub("\"", "\\\"" )

			k = string.gsub(k, "'", "\\\'" )
			text = text .. tab .. '["' .. k .. '"] = '
		else
			text = text .. tab .. "[" .. k .. "] = "
		end
		if type(v) == "string" then
			v = string.gsub(v, "\n", "\\\n" )

			
			v = v:gsub("\\\"", "\\\\\\\"" )
			v = v:gsub("\"", "\\\"" )
			
			v = string.gsub(v, "'", "\\\'" )
			text = text .. '"' .. v .. '",\n'..crlf
		elseif type(v) == "number" then
			text = text .. v .. ",\n"..crlf
		elseif type(v) == "table" then
			text = text .. tableToString(v, i + 1)
		elseif type(v) == "boolean" then
			if v == true then
				text = text .. "true,\n"..crlf
			else
				text = text .. "false,\n"..crlf
			end
		elseif type(v) == "function" then
			text = text .. v .. ",\n"..crlf
		elseif v == nil then
			text = text .. "nil,\n"..crlf
		end
	end
	tab = ""
	for n = 1, i do																	
		tab = tab .. "\t"
	end
	if i == 0 then
		text = text .. tab .. "}\n"		..crlf											
	else
		text = text .. tab .. "},\n"	..crlf												
	end
	return text
end


function removeLastNumberAfterDash(str)
    -- Supprime le dernier groupe de chiffres après un tiret
    return string.gsub(str, "%-?%d+$", "")
end

function generatGroupId()

	local maxiId_INI = maxiId
    local idTemp = 1
    local loop = 1
	local maxLoop = 500
	
    repeat
        idTemp = math.random(1, maxiId)
		loop = loop+1
    until not allGroupId[idTemp] or loop > maxLoop

	local loopB = 1
    if loop >= maxLoop then  
        idTemp = maxiId
        repeat
            idTemp = idTemp + 1
            loopB = loopB+1	
			-- print('A IN generatGroupId /loopB  : '..loopB.." /maxiIdTEST: "..maxiId_INI.." /maxiId: "..maxiId_INI.." /idTemp: "..idTemp )
        until not allGroupId[idTemp] or loopB > maxLoop
		-- print('B SORTIE generatGroupId /loopB  : '..loopB.." /maxiIdTEST: "..maxiId_INI.." /maxiId: "..maxiId_INI.." /idTemp: "..idTemp )
    end

	-- if maxiId < idTemp+10 then maxiId = idTemp+10 end
	if maxiId < idTemp then maxiId = idTemp end


	if loopB >= maxLoop then
		-- 	print('generatGroupId /loopB  : '..loopB.." /maxiIdTEST: "..maxiId_INI.." /maxiId: "..maxiId_INI.." /idTemp: "..idTemp )
		print("Warning: infinite loop in function generatUnitId()")
	end

	allGroupId[idTemp] = true
    return idTemp
end

function generatUnitId()

	local maxiIdTEST = maxiId
	local idTemp = 1
	local loop = 1
	local maxLoop = 500
	
	repeat
		idTemp = math.random(1, maxiId)
		loop = loop+1
	until not allUnitId[idTemp] or loop > maxLoop

	local loopB = 1
	if loop >= maxLoop then  
		idTemp = maxiId
		repeat
			idTemp = idTemp + 1
			loopB = loopB+1	
		until not allUnitId[idTemp] or loopB > maxLoop
	end

	-- if maxiId < idTemp+10 then maxiId = idTemp+10 end
	if maxiId < idTemp then maxiId = idTemp end
	
	if loopB >= maxLoop then
		-- print('generatUnitId /loopB  : '..loopB.." /maxiIdTEST: "..maxiIdTEST.." /maxiId: "..maxiId.." /idTemp: "..idTemp )
		print("Warning: infinite loop in function generatUnitId()")
	end

	allUnitId[idTemp] = true
	return idTemp
end

-- Fonction pour générer un nouveau nom unique
function generateNewSingleName(allName, originalName)
    
    -- print("generateNewSingleName AA "..originalName)
    
    local newName = originalName
    local numberPattern = "(.-)(%-(%d+))$"  -- Modèle pour capturer le nom de base et le numéro final (ex: "EAU West Front-2" -> "EAU West Front", "-2", "2")
    local baseName, suffix, currentNumber = string.match(originalName, numberPattern)  -- Extraire le nom de base et le numéro (s'il existe)
    
    if currentNumber then
        -- Si un numéro existe déjà, incrémentez-le
        currentNumber = tonumber(currentNumber) + 1
        newName = baseName .. "-" .. currentNumber  -- Construire le nouveau nom avec le numéro incrémenté
    else
        -- S'il n'y a pas de numéro à la fin, ajoutez "-1"
        newName = originalName .. "-1"
    end

    local loop = 1
    while allName[newName] and loop <= 5000 do
        -- Tant que le nom existe déjà, continuez à incrémenter le numéro
        baseName, suffix, currentNumber = string.match(newName, numberPattern)
        if currentNumber then
            currentNumber = tonumber(currentNumber) + 1
            newName = baseName .. "-" .. currentNumber
        end
        loop = loop + 1
    end

    -- print("generateNewSingleName BB "..newName)

    return newName
end

-- Fonction pour effectuer une rotation autour d'un point
function rotatePointAroundCenter(point, center, angle)
	local radians = angle  
	local cosTheta = math.cos(radians)
	local sinTheta = math.sin(radians)

	-- Translation du point par rapport au centre
	local translatedX = point.x - center.x
	local translatedY = point.y - center.y

	-- Calculer la rotation
	local rotatedX = translatedX * cosTheta - translatedY * sinTheta
	local rotatedY = translatedX * sinTheta + translatedY * cosTheta

	-- Translation inverse pour revenir au centre
	return { x = rotatedX + center.x, y = rotatedY + center.y }
end

-- Fonction principale pour tourner le groupe d'unités autour d'un point de référence donné
function rotateGroupAroundPoint(point, referencePoint, angle)

	local newPoint = rotatePointAroundCenter(point, referencePoint, angle)
	point.x = newPoint.x
	point.y = newPoint.y
    -- Ajuster le heading (direction) de l'unité
    if point.heading then
        point.heading = (point.heading + angle) % (2 * math.pi)  -- Garder l'angle entre 0 et 2?
    end

	return point.x, point.y, point.heading

end

function addInmapResource(mapResource)
    local foundAstiRef = false
    local max = 0

    -- Parcours de chaque clé et valeur de la table
    for fileName, file in pairs(mapResource) do
        -- Vérifie si le fichier est "ASTI_ref"
        if file == "ASTI_ref" then
            foundAstiRef = true
            break
        end
        
        -- Utilise une regex pour extraire le numéro à la fin de chaque clé
        local keyNumber = string.match(fileName, "_(%d+)$")
        
        if keyNumber then
            keyNumber = tonumber(keyNumber)  -- Convertit en nombre pour comparaison
            if keyNumber > max then
                max = keyNumber  -- Met à jour le max si nécessaire
            end
        end
    end

    -- Si "ASTI_ref" n'est pas trouvé, ajoute une nouvelle entrée avec un numéro incrémenté
    if not foundAstiRef then
        mapResource["ResKey_Action_"..(max + 1)] = "ASTI_ref"
    end

    return mapResource
end



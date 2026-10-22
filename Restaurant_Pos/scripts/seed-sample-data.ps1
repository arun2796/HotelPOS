<#
.SYNOPSIS
    Loads a sample floor plan (sections and tables) and a sample Indian restaurant menu into a HotelPOS server.

.DESCRIPTION
    Talks to the API like a terminal does, so it works against any server you can reach: sign in as an
    administrator, create what is missing, skip what already exists (safe to run twice). Edit the $Sections and $Menu
    blocks below to match the venue, or delete items afterwards in Administration -> Menu / Floor.

    The sign-in registers a terminal called SEED-01; deactivate it afterwards under Administration -> Devices so it
    does not hold a licence seat.

.EXAMPLE
    .\seed-sample-data.ps1 -ApiBaseUrl http://192.168.1.29:5000 -Username admin
    (asks for the password)
#>
[CmdletBinding()]
param(
    [string] $ApiBaseUrl = 'http://localhost:5000',
    [string] $Username = 'admin',
    [string] $Password,
    [switch] $SkipMenu,
    [switch] $SkipFloor
)

$ErrorActionPreference = 'Stop'
if (-not $Password) {
    $secure = Read-Host -AsSecureString "Password for $Username"
    $Password = [Runtime.InteropServices.Marshal]::PtrToStringUni([Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($secure))
}
$base = $ApiBaseUrl.TrimEnd('/')

# ---------------------------------------------------------------- sample data (edit freely) ----------------------
# Section name -> list of tables: code, seats. Codes must be unique across the whole venue.
$Sections = [ordered]@{
    'AC Hall'     = @(1..8 | ForEach-Object { @{ Code = ('T{0:00}' -f $_); Capacity = 4 } }) + @(@{ Code = 'T09'; Capacity = 6 }, @{ Code = 'T10'; Capacity = 6 })
    'Non-AC Hall' = @(11..20 | ForEach-Object { @{ Code = ('T{0:00}' -f $_); Capacity = 4 } })
    'Family Room' = @(1..4 | ForEach-Object { @{ Code = ('F{0:00}' -f $_); Capacity = 8 } })
    'Outdoor'     = @(1..4 | ForEach-Object { @{ Code = ('O{0:00}' -f $_); Capacity = 4 } })
}

# Category -> items: code, name, price (INR). GST 5% is applied when a tax with "5" in its name exists.
$Menu = [ordered]@{
    'Tiffin' = @(
        @('TF01', 'Idli (2 pcs)', 40), @('TF02', 'Medu Vada (2 pcs)', 50), @('TF03', 'Pongal', 60), @('TF04', 'Upma', 50),
        @('TF05', 'Poori (2 pcs) with Masala', 70), @('TF06', 'Idiyappam (3 pcs)', 60), @('TF07', 'Appam (2 pcs)', 60),
        @('TF08', 'Uthappam Plain', 70), @('TF09', 'Onion Uthappam', 85), @('TF10', 'Rava Kichadi', 60))
    'Dosa' = @(
        @('DS01', 'Plain Dosa', 60), @('DS02', 'Masala Dosa', 85), @('DS03', 'Ghee Roast', 110), @('DS04', 'Onion Dosa', 80),
        @('DS05', 'Rava Dosa', 90), @('DS06', 'Rava Masala Dosa', 110), @('DS07', 'Paneer Dosa', 130), @('DS08', 'Set Dosa (3 pcs)', 80),
        @('DS09', 'Podi Dosa', 90), @('DS10', 'Cheese Dosa', 130))
    'Rice & Meals' = @(
        @('RM01', 'South Indian Meals', 150), @('RM02', 'Special Meals', 220), @('RM03', 'Curd Rice', 80), @('RM04', 'Sambar Rice', 90),
        @('RM05', 'Lemon Rice', 90), @('RM06', 'Tamarind Rice', 90), @('RM07', 'Veg Biryani', 180), @('RM08', 'Chicken Biryani', 240),
        @('RM09', 'Mutton Biryani', 320), @('RM10', 'Egg Biryani', 190), @('RM11', 'Jeera Rice', 120), @('RM12', 'Steamed Rice', 70))
    'Curries' = @(
        @('CU01', 'Dal Tadka', 140), @('CU02', 'Paneer Butter Masala', 220), @('CU03', 'Kadai Paneer', 230), @('CU04', 'Palak Paneer', 220),
        @('CU05', 'Chana Masala', 160), @('CU06', 'Mixed Veg Kurma', 170), @('CU07', 'Chicken Chettinad', 260), @('CU08', 'Butter Chicken', 280),
        @('CU09', 'Mutton Curry', 320), @('CU10', 'Fish Curry', 280), @('CU11', 'Egg Curry', 160))
    'Breads' = @(
        @('BR01', 'Chapati (2 pcs)', 40), @('BR02', 'Parotta (2 pcs)', 60), @('BR03', 'Butter Naan', 50), @('BR04', 'Garlic Naan', 60),
        @('BR05', 'Tandoori Roti', 35), @('BR06', 'Kulcha', 60), @('BR07', 'Kothu Parotta Veg', 140), @('BR08', 'Kothu Parotta Chicken', 190))
    'Starters' = @(
        @('ST01', 'Gobi 65', 150), @('ST02', 'Paneer 65', 190), @('ST03', 'Chicken 65', 220), @('ST04', 'Chilli Chicken', 230),
        @('ST05', 'Mushroom Pepper Fry', 180), @('ST06', 'Veg Manchurian', 160), @('ST07', 'Chicken Lollipop (4 pcs)', 240), @('ST08', 'Fish Fry', 260))
    'Chinese' = @(
        @('CH01', 'Veg Fried Rice', 150), @('CH02', 'Egg Fried Rice', 170), @('CH03', 'Chicken Fried Rice', 200), @('CH04', 'Veg Noodles', 150),
        @('CH05', 'Egg Noodles', 170), @('CH06', 'Chicken Noodles', 200), @('CH07', 'Schezwan Fried Rice', 180))
    'Beverages' = @(
        @('BV01', 'Filter Coffee', 30), @('BV02', 'Tea', 20), @('BV03', 'Badam Milk', 60), @('BV04', 'Fresh Lime Soda', 60),
        @('BV05', 'Buttermilk', 30), @('BV06', 'Mango Lassi', 90), @('BV07', 'Sweet Lassi', 70), @('BV08', 'Mineral Water 1L', 20),
        @('BV09', 'Soft Drink 300ml', 40), @('BV10', 'Fresh Juice', 90))
    'Desserts' = @(
        @('DE01', 'Gulab Jamun (2 pcs)', 60), @('DE02', 'Payasam', 70), @('DE03', 'Kesari', 50), @('DE04', 'Ice Cream (scoop)', 60),
        @('DE05', 'Rasmalai (2 pcs)', 90), @('DE06', 'Jigarthanda', 90))
}
# ------------------------------------------------------------------------------------------------------------------

function Invoke-Api([string] $method, [string] $path, $body = $null) {
    $params = @{ Method = $method; Uri = "$base/$path"; Headers = @{ Authorization = "Bearer $script:token" }; ContentType = 'application/json'; TimeoutSec = 30 }
    if ($null -ne $body) { $params.Body = ($body | ConvertTo-Json -Depth 5) }
    try {
        return Invoke-RestMethod @params
    } catch {
        $detail = $_.ErrorDetails.Message
        if ($detail) { try { $envelope = $detail | ConvertFrom-Json; $detail = (($envelope.errors | ForEach-Object { $_.message }) -join '; '); if (-not $detail) { $detail = $envelope.message } } catch { } }
        throw "$method $path failed: $($_.Exception.Message) $detail"
    }
}

# Sign in (terminal SEED-01, type Admin = 4).
$login = Invoke-RestMethod -Method Post -Uri "$base/api/auth/login" -ContentType 'application/json' -TimeoutSec 30 `
    -Body (@{ username = $Username; password = $Password; deviceName = 'SEED-01'; deviceType = 4 } | ConvertTo-Json)
if (-not $login.success) { throw "Sign-in failed: $($login.message)" }
$script:token = $login.data.accessToken
if ($login.data.user.mustChangePassword) { throw "Sign in once in the HotelPOS app and change the administrator's password first; the server allows nothing else until then." }
Write-Host "Signed in to $base as $Username."

$created = @{ Sections = 0; Tables = 0; Categories = 0; Items = 0 }

if (-not $SkipFloor) {
    $existingSections = @((Invoke-Api GET 'api/sections').data)
    # api/tables answers the floor map: sections with their tables.
    $existingTables = @((Invoke-Api GET 'api/tables').data.sections | ForEach-Object { $_.tables })
    $order = 0
    foreach ($name in $Sections.Keys) {
        $order++
        $section = $existingSections | Where-Object { $_.name -eq $name } | Select-Object -First 1
        if (-not $section) {
            $section = (Invoke-Api POST 'api/sections' @{ name = $name; sortOrder = $order }).data
            $created.Sections++
        }
        foreach ($t in $Sections[$name]) {
            if ($existingTables | Where-Object { $_.code -eq $t.Code }) { continue }
            Invoke-Api POST 'api/tables' @{ code = $t.Code; sectionId = $section.id; capacity = $t.Capacity } | Out-Null
            $created.Tables++
        }
        Write-Host "Section '$name': $($Sections[$name].Count) tables."
    }
}

if (-not $SkipMenu) {
    $stations = @((Invoke-Api GET 'api/stations').data) | Where-Object { $_.isActive -ne $false }
    if ($stations.Count -eq 0) { throw 'No preparation station exists; create one under Administration -> Kitchen stations first.' }
    $stationId = $stations[0].id
    $taxes = @((Invoke-Api GET 'api/taxes').data)
    $gst5 = $taxes | Where-Object { $_.ratePercent -eq 5 -and $_.isActive -ne $false } | Select-Object -First 1
    if (-not $gst5) {
        $gst5 = (Invoke-Api POST 'api/taxes' @{ name = 'GST 5%'; code = 'GST5'; ratePercent = 5; isActive = $true }).data
        Write-Host 'Created tax GST 5%.'
    }
    $taxId = $gst5.id
    Write-Host ("Station: {0}; tax: {1}." -f $stations[0].name, $gst5.name)

    $existingCategories = @((Invoke-Api GET 'api/categories').data)
    $existingItems = @((Invoke-Api GET 'api/menu-items').data)
    $order = 0
    foreach ($categoryName in $Menu.Keys) {
        $order++
        $category = $existingCategories | Where-Object { $_.name -eq $categoryName } | Select-Object -First 1
        if (-not $category) {
            $category = (Invoke-Api POST 'api/categories' @{ name = $categoryName; sortOrder = $order }).data
            $created.Categories++
        }
        $sort = 0
        foreach ($row in $Menu[$categoryName]) {
            $sort++
            $code, $itemName, $price = $row
            if ($existingItems | Where-Object { $_.code -eq $code -or $_.name -eq $itemName }) { continue }
            $body = @{ categoryId = $category.id; name = $itemName; code = $code; price = $price; taxId = $taxId; preparationStationId = $stationId; sortOrder = $sort; isAvailable = $true }
            Invoke-Api POST 'api/menu-items' $body | Out-Null
            $created.Items++
        }
        Write-Host "Category '$categoryName': $($Menu[$categoryName].Count) items."
    }
}

Write-Host ("Done. Created {0} sections, {1} tables, {2} categories, {3} items (existing ones were kept)." -f $created.Sections, $created.Tables, $created.Categories, $created.Items)
Write-Host "Deactivate terminal SEED-01 under Administration -> Devices when you are finished."

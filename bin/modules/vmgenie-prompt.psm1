Import-Module "$PSScriptRoot\vmgenie-client.psm1"
Import-Module "$PSScriptRoot\vmgenie-import.psm1"
Import-Sharprompt

function Invoke-UsernamePrompt {
    param (
        [string] $value,
        [string] $label
    )

    if ([string]::IsNullOrWhiteSpace($label)) {
        $label = 'User Name'
    }

    return [Sharprompt.Prompt]::Input[string](
        $label,
        $value
    )
}

function Invoke-InstancePrompt {
    param (
        [string] $value,
        [string] $label
    )

    if ([string]::IsNullOrWhiteSpace($label)) {
        $label = 'Instance Name'
    }

    return [Sharprompt.Prompt]::Input[string](
        $label,
        $value
    )
}

function Invoke-HostnamePrompt {
    param (
        [string] $value,
        [string] $label
    )

    if ([string]::IsNullOrWhiteSpace($label)) {
        $label = 'Host Name'
    }

    return [Sharprompt.Prompt]::Input[string](
        $label,
        $value
    )
}

function Invoke-TimezonePrompt {
    param (
        [string] $value,
        [string] $label
    )

    if ([string]::IsNullOrWhiteSpace($label)) {
        $label = 'Time Zone'
    }

    # Define the IANA timezones (keys of the map)
    $timezones = [System.Collections.Generic.List[string]]::new()
    @(
        "Etc/GMT+12",
        "Pacific/Pago_Pago",
        "Pacific/Honolulu",
        "America/Anchorage",
        "America/Los_Angeles",
        "America/Denver",
        "America/Chicago",
        "America/New_York",
        "America/Halifax",
        "America/Argentina/Buenos_Aires",
        "Atlantic/South_Georgia",
        "Atlantic/Azores",
        "Etc/UTC",
        "Europe/Paris",
        "Europe/Athens",
        "Europe/Moscow",
        "Asia/Dubai",
        "Asia/Karachi",
        "Asia/Dhaka",
        "Asia/Bangkok",
        "Asia/Shanghai",
        "Asia/Tokyo",
        "Australia/Sydney",
        "Pacific/Guadalcanal",
        "Pacific/Auckland",
        "Pacific/Tongatapu",
        "Pacific/Kiritimati"
    ) | ForEach-Object { $timezones.Add($_) }

    # If a current value exists and is in the list, use it as default
    if ($value -and $timezones.Contains($value)) {
        $default = $value
    }
    else {
        $default = $null
    }

    # Prepare SelectOptions
    $options = [Sharprompt.SelectOptions[string]]::new()
    $options.Message = $label
    $options.Items = $timezones
    $options.DefaultValue = $default
    $options.PageSize = 27  # adjust as you like

    [Sharprompt.Prompt]::Select[string]($options)
}


function Invoke-LayoutPrompt {
    param (
        [string] $value,
        [string] $label
    )

    if ([string]::IsNullOrWhiteSpace($label)) {
        $label = 'Keyboard Layout'
    }

    # common layouts — you can add more if needed
    $layouts = @(
        'us',           # United States
        'gb',           # United Kingdom
        'de',           # Germany
        'fr',           # France
        'es',           # Spain
        'it',           # Italy
        'pt',           # Portugal
        'br',           # Brazil
        'jp',           # Japan
        'ru',           # Russia
        'se',           # Sweden
        'fi',           # Finland
        'no',           # Norway
        'dk',           # Denmark
        'pl',           # Poland
        'cz',           # Czech Republic
        'hu',           # Hungary'
        'ca',           # Canada (French)
        'ch',           # Switzerland
        'be',           # Belgium
        'tr',           # Turkey
        'gr'            # Greece
    )

    # If the current value is valid, use it as default
    $default = if ($value -and $layouts -contains $value) { $value } else { $null }

    $options = New-Object Sharprompt.SelectOptions[string]
    $options.Message = $label
    $options.Items = [System.Collections.Generic.List[string]]::new()
    $layouts | ForEach-Object { $options.Items.Add($_) }
    $options.DefaultValue = $default
    $options.PageSize = 22

    return [Sharprompt.Prompt]::Select[string]($options)
}

function Invoke-LocalePrompt {
    param (
        [string] $value,
        [string] $label
    )

    if ([string]::IsNullOrWhiteSpace($label)) {
        $label = 'Locale'
    }

    # Common locales — you can extend this list as needed
    $locales = @(
        'en_US.UTF-8',   # US English
        'en_GB.UTF-8',   # UK English
        'fr_FR.UTF-8',   # French
        'de_DE.UTF-8',   # German
        'es_ES.UTF-8',   # Spanish
        'it_IT.UTF-8',   # Italian
        'pt_PT.UTF-8',   # Portuguese
        'pt_BR.UTF-8',   # Brazilian Portuguese
        'ru_RU.UTF-8',   # Russian
        'ja_JP.UTF-8',   # Japanese
        'zh_CN.UTF-8',   # Chinese (Simplified)
        'zh_TW.UTF-8',   # Chinese (Traditional)
        'ko_KR.UTF-8',   # Korean
        'nl_NL.UTF-8',   # Dutch
        'sv_SE.UTF-8',   # Swedish
        'fi_FI.UTF-8',   # Finnish
        'no_NO.UTF-8',   # Norwegian
        'pl_PL.UTF-8',   # Polish
        'cs_CZ.UTF-8',   # Czech
        'hu_HU.UTF-8',   # Hungarian
        'tr_TR.UTF-8',   # Turkish
        'el_GR.UTF-8'    # Greek
    )

    # Determine if the current value is valid and set it as default
    $default = if ($value -and $locales -contains $value) { $value } else { $null }

    $options = New-Object Sharprompt.SelectOptions[string]
    $options.Message = $label
    $options.Items = [System.Collections.Generic.List[string]]::new()
    $locales | ForEach-Object { $options.Items.Add($_) }
    $options.DefaultValue = $default
    $options.PageSize = 22

    return [Sharprompt.Prompt]::Select[string]($options)
}

function Invoke-OperatingSystemPrompt {
    param (
        [string] $default = $null,
        [string] $label = 'Operating System'
    )

    # Send the event and block until we get a response
    $script:OperatingSystemsResult = $null
    $script:OperatingSystemsError = $null

    $result = Send-Event -Command 'operating-system' -Parameters @{ action = 'list' } -Handler {
        param ($Response)

        if ($Response.status -ne 'ok') {
            # Record the error so outer code can throw explicitly
            $script:OperatingSystemsError = $Response.data.details
            Complete-Request -Id $Response.id
            return
        }

        $script:OperatingSystemsResult = $Response.data.operatingSystems
        Complete-Request -Id $Response.id
    }

    if (-not $result) {
        throw "Failed to retrieve operating systems: service did not respond or response was invalid."
    }

    if ($script:OperatingSystemsError) {
        throw "Service error: $script:OperatingSystemsError"
    }

    if (-not $script:OperatingSystemsResult -or $script:OperatingSystemsResult.Count -eq 0) {
        throw "No operating systems available to select."
    }

    # Prepare the SharpPrompt select
    $options = New-Object Sharprompt.SelectOptions[string]
    $options.Message = $label
    $options.Items = [System.Collections.Generic.List[string]]::new()
    $script:OperatingSystemsResult | ForEach-Object { $options.Items.Add($_) }

    if ($default -and $options.Items.Contains($default)) {
        $options.DefaultValue = $default
    }

    $options.PageSize = $options.Items.Count

    return [Sharprompt.Prompt]::Select[string]($options)
}

function Invoke-OsVersionPrompt {
    param (
        [Parameter(Mandatory)]
        [string] $OperatingSystem,

        [string] $default = $null,
        [string] $label = 'Operating System Version'
    )

    $script:OsVersionsResult = $null
    $script:OsVersionsError = $null

    $result = Send-Event -Command 'os-version' -Parameters @{ action = 'list'; os = $OperatingSystem } -Handler {
        param ($Response)

        if ($Response.status -ne 'ok') {
            $script:OsVersionsError = $Response.data.details
            Complete-Request -Id $Response.id
            return
        }

        $script:OsVersionsResult = $Response.data.osVersions
        Complete-Request -Id $Response.id
    }

    if (-not $result) {
        throw "Failed to retrieve operating system versions: service did not respond or response was invalid."
    }

    if ($script:OsVersionsError) {
        throw "Service error: $script:OsVersionsError"
    }

    if (-not $script:OsVersionsResult -or $script:OsVersionsResult.Count -eq 0) {
        Write-Warning "No versions found for operating system: $OperatingSystem"
        return $null
    }

    $options = New-Object Sharprompt.SelectOptions[string]
    $options.Message = $label
    $options.Items = [System.Collections.Generic.List[string]]::new()
    $script:OsVersionsResult | ForEach-Object { $options.Items.Add($_) }

    # if no valid $default provided, use the first element
    if ($default -and $options.Items.Contains($default)) {
        $options.DefaultValue = $default
    }
    else {
        $options.DefaultValue = $options.Items[0]
    }

    $options.PageSize = $options.Items.Count

    return [Sharprompt.Prompt]::Select[string]($options)
}

function Invoke-VmPrompt {
    param (
        [string] $label = 'Select Virtual Machine',
        [string] $Os,
        [string] $Version
    )

    $script:VmResult = $null
    $script:VmError = $null

    # Query the service for the VM list
    $result = Send-Event -Command 'vm' -Parameters @{ action = 'list' } -Handler {
        param ($Response)

        if ($Response.status -ne 'ok') {
            # Use the raw data string for the error message
            $script:VmError = $Response.data
            Complete-Request -Id $Response.id
            return
        }

        $script:VmResult = $Response.data.vms
        Complete-Request -Id $Response.id
    }

    if (-not $result) {
        throw "Failed to retrieve VM list: service did not respond or response was invalid."
    }

    if ($script:VmError) {
        throw "Service error: $script:VmError"
    }

    if (-not $script:VmResult -or $script:VmResult.Count -eq 0) {
        throw "No VMs returned from service."
    }

    # Build a hashtable: display name → VM object
    $vmMap = @{}

    foreach ($vm in $script:VmResult) {
        $displayName = $vm.Name
        $vmMap[$displayName] = $vm
    }

    # Prepare SharpPrompt
    $options = New-Object Sharprompt.SelectOptions[string]
    $options.Message = $label
    $options.Items = [System.Collections.Generic.List[string]]::new()
    $vmMap.Keys | ForEach-Object { $options.Items.Add($_) }

    $options.PageSize = $options.Items.Count

    # Compute default
    $defaultVm = $null

    if ($Os -or $Version) {
        $bestMatch = $options.Items |
        Sort-Object {
            $score = 0
            if ($Os -and ($_ -match [regex]::Escape($Os))) { $score++ }
            if ($Version -and ($_ -match [regex]::Escape($Version))) { $score++ }
            -1 * $score  # negate so higher score is sorted first
        } |
        Select-Object -First 1

        if ($bestMatch) {
            $defaultVm = $bestMatch
        }
    }

    if (-not $defaultVm) {
        $defaultVm = $options.Items[0]
    }

    $options.DefaultValue = $defaultVm

    $selectedName = [Sharprompt.Prompt]::Select[string]($options)

    return $vmMap[$selectedName]
}

Export-ModuleMember -Function `
    Invoke-UsernamePrompt, `
    Invoke-TimezonePrompt, `
    Invoke-LayoutPrompt, `
    Invoke-LocalePrompt, `
    Invoke-OperatingSystemPrompt, `
    Invoke-OsVersionPrompt, `
    Invoke-VmPrompt, `
    Invoke-InstancePrompt, `
    Invoke-HostnamePrompt

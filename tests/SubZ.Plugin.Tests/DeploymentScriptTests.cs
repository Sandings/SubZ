namespace SubZ.Plugin.Tests;

public sealed class DeploymentScriptTests
{
    [Fact]
    public void DeployScriptPrefersBackupEndpointByDefault()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy-unraid.ps1"));

        Assert.Contains("$BackupRemoteHost = \"\"", source);
        Assert.Contains("$BackupRemotePort = 0", source);
        Assert.Contains("$DeployInfoPath = \"\"", source);
        Assert.Contains("$explicitRemoteHost = $PSBoundParameters.ContainsKey(\"RemoteHost\")", source);
        Assert.Contains("Import-DeployInfo -Path $DeployInfoPath", source);
        Assert.Contains("(Get-PropertyValue $_ \"Name\") -match \"backup|备用\"", source);
        Assert.Contains("$backupRow = $connectionRows | Select-Object -First 1", source);
    }

    [Fact]
    public void DeployScriptCanReadMarkdownDeployInfoWithoutPrintingSecrets()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy-unraid.ps1"));

        Assert.Contains("function Read-MarkdownTables", source);
        Assert.Contains("function Import-DeployInfo", source);
        Assert.Contains("Get-PropertyValue $_ \"Password\"", source);
        Assert.Contains("(Get-PropertyValue $_ \"Name\") -match \"^Unraid$\"", source);
        Assert.Contains("(Get-PropertyValue $_ \"Type\") -eq \"ssh-ed25519\"", source);
        Assert.Contains("SHA256:[A-Za-z0-9+/=]+", source);
        Assert.Contains("Deploy target: ${RemoteHost}:${RemotePort} as ${RemoteUser}", source);
        Assert.Contains("Credential source: password=$(-not [string]::IsNullOrWhiteSpace($RemotePassword)); hostKey=$(-not [string]::IsNullOrWhiteSpace($RemoteHostKey))", source);
        Assert.DoesNotContain("Write-Host $RemotePassword", source);
    }

    [Fact]
    public void DeployScriptUsesPuttyBeforeSshpassWhenPasswordIsAvailable()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy-unraid.ps1"));

        Assert.Contains("$usePuttyPassword = -not [string]::IsNullOrWhiteSpace($RemotePassword)", source);
        Assert.Contains("$useSshpass = -not $usePuttyPassword", source);
        Assert.Contains("$psi.ArgumentList.Add($arg)", source);
    }
}

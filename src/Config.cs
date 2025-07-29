using System;

using YamlDotNet.Serialization;

namespace VmGenie;

public class Config
{
    [YamlMember(Alias = "CREATED_AT")]
    public DateTime CreatedAt { get; set; }

    [YamlMember(Alias = "YAML_LIBRARY")]
    public string YamlLibrary { get; set; } = string.Empty;

    [YamlMember(Alias = "USERNAME")]
    public string Username { get; set; } = string.Empty;

    [YamlMember(Alias = "VM_SWITCH")]
    public string VmSwitch { get; set; } = string.Empty;

    [YamlMember(Alias = "TIMEZONE")]
    public string Timezone { get; set; } = string.Empty;

    [YamlMember(Alias = "LOCALE")]
    public string Locale { get; set; } = string.Empty;

    [YamlMember(Alias = "LAYOUT")]
    public string Layout { get; set; } = string.Empty;

    [YamlMember(Alias = "LOG_DIR")]
    public string LogDir { get; set; } = string.Empty;

    [YamlMember(Alias = "CLOUD_DIR")]
    public string CloudDir { get; set; } = string.Empty;

    [YamlMember(Alias = "GMI_DIR")]
    public string GmiDir { get; set; } = string.Empty;

    [YamlMember(Alias = "TEMPLATE_DIR")]
    public string TemplateDir { get; set; } = string.Empty;

    [YamlMember(Alias = "APPLICATION_DIR")]
    public string ApplicationDir { get; set; } = string.Empty;

    [YamlMember(Alias = "BIN")]
    public string Bin { get; set; } = string.Empty;

    [YamlMember(Alias = "VAR")]
    public string Var { get; set; } = string.Empty;

    [YamlMember(Alias = "ETC")]
    public string Etc { get; set; } = string.Empty;

    [YamlMember(Alias = "SRC")]
    public string Src { get; set; } = string.Empty;

    [YamlMember(Alias = "TMP")]
    public string Tmp { get; set; } = string.Empty;
}

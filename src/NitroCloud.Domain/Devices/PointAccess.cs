namespace NitroCloud.Domain.Devices;

/// <summary>
/// 点位读写权限（与 NitroGateway.Domain.Devices.PointAccess 对齐，值顺序一致：ReadOnly=0 / WriteOnly=1 / ReadWrite=2）。
/// 网关上行载荷携带该权限（数字或字符串均可，解析器枚举转换器双兼容），云端自动注册据此刻画点位可写性。
/// </summary>
public enum PointAccess
{
    /// <summary>只读（采集型点位，如传感器读数）</summary>
    ReadOnly,

    /// <summary>只写（控制型点位，如继电器开关）</summary>
    WriteOnly,

    /// <summary>可读写（如变频器频率设定，可读当前值也可下发目标值）</summary>
    ReadWrite
}

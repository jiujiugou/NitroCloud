using System.Text;
using NitroCloud.Ingest.Parsing;

namespace NitroCloud.UnitTests.Ingest;

public class __DiagTests
{
    [Fact]
    public void Debug_ParseMyJson()
    {
        var id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var idField = id is null ? "" : $"\"id\": \"{id}\",";
        var json = $$"""
            {
              "siteId": "site-1",
              {{idField}}
              "deviceId": "11111111-1111-1111-1111-111111111111",
              "records": [
                {
                  "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                  "deviceId": "11111111-1111-1111-1111-111111111111",
                  "devicePointId": "22222222-2222-2222-2222-222222222222",
                  "pointName": "Temp",
                  "value": 23.5,
                  "dataType": "Float",
                  "timestamp": "2026-08-23T01:00:00Z",
                  "quality": "Good"
                }
              ]
            }
            """;

        var result = new MeasurementBatchParser().Parse(Encoding.UTF8.GetBytes(json), "site-1");
        Assert.True(result.IsSuccess, $"parse failed: {result.Error} | json={json}");
    }
}

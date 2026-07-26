using Axon.Control.Tests.Deploy;

namespace Axon.Control.Tests.Scripts;

public sealed class NetworkAuditContractTests
{
    [Fact]
    public void Capture_is_scoped_to_Axon_http_and_records_reproducible_context()
    {
        var capture = DeployTestFiles.Read("scripts/network-audit/Start-AxonTrafficAudit.ps1");

        Assert.Contains("#Requires -RunAsAdministrator", capture);
        Assert.Contains("Get-NetIPAddress -AddressFamily IPv4 -IPAddress $BindIp", capture);
        Assert.Contains("Pass -BundleRoot", capture);
        Assert.Contains("pktmon.exe filter add AxonMatrix -i $BindIp -p 80 -t TCP", capture);
        Assert.Contains("--comp nics --type flow", capture);
        Assert.Contains("--pkt-size $PacketBytes", capture);
        Assert.Contains("pktmon.exe etl2pcap", capture);
        Assert.Contains("Get-NetAdapterStatistics", capture);
        Assert.Contains("Get-NetTCPConnection", capture);
        Assert.Contains("docker stats --no-stream", capture);
        Assert.Contains("synapse-processed-requests.log", capture);
        Assert.Contains("Processed request:", capture);
        Assert.Contains("packetPayloadWarning", capture);
        Assert.DoesNotContain("Invoke-WebRequest", capture, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Summary_distinguishes_matrix_from_comparison_traffic()
    {
        var summary = DeployTestFiles.Read("scripts/network-audit/Summarize-AxonPcap.ps1");
        var guide = DeployTestFiles.Read("docs/operator/NETWORK_BANDWIDTH_AUDIT.md");

        Assert.Contains("frame.len", summary);
        Assert.Contains("tcp.len", summary);
        Assert.Contains("tcp.analysis.retransmission", summary);
        Assert.Contains("ComparisonUdpPort", summary);
        Assert.Contains("ComparisonMulticastAddress", summary);
        Assert.Contains("trafficClasses", summary);
        Assert.Contains("comparison = [ordered]@", summary);
        Assert.Contains("inboundWireBytes", summary);
        Assert.Contains("outboundWireBytes", summary);
        Assert.Contains("Ethernet/IP capture values", summary);
        Assert.Contains("idle-two-clients", guide);
        Assert.Contains("Enable nginx gzip", guide);
        Assert.Contains("Explicitly disable Synapse push calculation", guide);
        Assert.Contains("Shared-path capture with comparison traffic", guide);
    }

    [Fact]
    public void Audit_kit_builder_is_offline_and_includes_capture_analysis_and_guide()
    {
        var builder = DeployTestFiles.Read("scripts/Build-NetworkAuditKit.ps1");

        Assert.Contains("Start-AxonTrafficAudit.ps1", builder);
        Assert.Contains("Summarize-AxonPcap.ps1", builder);
        Assert.Contains("NETWORK_BANDWIDTH_AUDIT.md", builder);
        Assert.Contains("README FIRST.txt", builder);
        Assert.Contains("schemaVersion = 1", builder);
        Assert.Contains("Compress-Archive", builder);
        Assert.DoesNotContain("Invoke-WebRequest", builder, StringComparison.OrdinalIgnoreCase);
    }
}

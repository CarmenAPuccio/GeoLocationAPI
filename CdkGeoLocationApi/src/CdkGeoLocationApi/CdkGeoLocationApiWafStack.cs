using Constructs;
using Amazon.CDK;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.WAFv2;
using static Amazon.CDK.AWS.WAFv2.CfnWebACL;
using System.Collections.Generic;

namespace CdkGeoLocationApi
{
    class ResourceNestedStackProps : NestedStackProps
    {
        public string ALBResourceArn { get; set; }
    }
    class CdkGeoLocationApiWafStack : NestedStack
    {
        public CdkGeoLocationApiWafStack(Construct scope, string id, ResourceNestedStackProps props) : base(scope, id, props)
        {
            // Create the WAFv2 WebACL
            CfnWebACL geoLocationAPIWAFWebACL = new CfnWebACL(this, "GeoLocationAPIWAFWebACL", new CfnWebACLProps
            {
                Name = "GeoLocationAPIWAFWebACL",
                Description = "GeoLocationAPIWAFWebACL",
                DefaultAction = new DefaultActionProperty
                {
                    Block = new BlockActionProperty { }
                },
                Scope = "REGIONAL",
                VisibilityConfig = new VisibilityConfigProperty
                {
                    CloudWatchMetricsEnabled = true,
                    MetricName = "GeoLocationAPIWAFWebACL",
                    SampledRequestsEnabled = true
                },
                Rules = new[]
                {
                    new RuleProperty
                    {
                        Name = "AWS-AWSManagedRulesCommonRuleSet",
                        Priority = 0,
                        OverrideAction = new OverrideActionProperty
                        {
                            None = new Dictionary<string, object> { }
                        },
                        Statement = new StatementProperty
                        {
                            ManagedRuleGroupStatement = new ManagedRuleGroupStatementProperty
                            {
                                VendorName = "AWS",
                                Name = "AWSManagedRulesCommonRuleSet"
                            }
                        },
                        VisibilityConfig = new VisibilityConfigProperty
                        {
                            CloudWatchMetricsEnabled = true,
                            MetricName = "AWS-AWSManagedRulesCommonRuleSet",
                            SampledRequestsEnabled = true
                        },
                    },

                    new RuleProperty
                    {
                        Name = "AWS-AWSManagedRulesKnownBadInputsRuleSet",
                        Priority = 1,
                        OverrideAction = new OverrideActionProperty
                        {
                            None = new Dictionary<string, object> { }
                        },
                        Statement = new StatementProperty
                        {
                            ManagedRuleGroupStatement = new ManagedRuleGroupStatementProperty
                            {
                                VendorName = "AWS",
                                Name = "AWSManagedRulesKnownBadInputsRuleSet"
                            }
                        },
                        VisibilityConfig = new VisibilityConfigProperty
                        {
                            CloudWatchMetricsEnabled = true,
                            MetricName = "AWS-AWSManagedRulesKnownBadInputsRuleSet",
                            SampledRequestsEnabled = true
                        },
                    },

                    new RuleProperty
                    {
                        Name = "AWS-AWSManagedRulesLinuxRuleSet",
                        Priority = 2,
                        OverrideAction = new OverrideActionProperty
                        {
                            None = new Dictionary<string, object> { }
                        },
                        Statement = new StatementProperty
                        {
                            ManagedRuleGroupStatement = new ManagedRuleGroupStatementProperty
                            {
                                VendorName = "AWS",
                                Name = "AWSManagedRulesLinuxRuleSet"
                            }
                        },
                        VisibilityConfig = new VisibilityConfigProperty
                        {
                            CloudWatchMetricsEnabled = true,
                            MetricName = "AWS-AWSManagedRulesLinuxRuleSet",
                            SampledRequestsEnabled = true
                        },
                    },

                    new RuleProperty
                    {
                        Name = "AWS-AWSManagedRulesUnixRuleSet",
                        Priority = 3,
                        OverrideAction = new OverrideActionProperty
                        {
                            None = new Dictionary<string, object> { }
                        },
                        Statement = new StatementProperty
                        {
                            ManagedRuleGroupStatement = new ManagedRuleGroupStatementProperty
                            {
                                VendorName = "AWS",
                                Name = "AWSManagedRulesUnixRuleSet"
                            }
                        },
                        VisibilityConfig = new VisibilityConfigProperty
                        {
                            CloudWatchMetricsEnabled = true,
                            MetricName = "AWS-AWSManagedRulesUnixRuleSet",
                            SampledRequestsEnabled = true
                        },
                    },

                    new RuleProperty
                    {
                        Name = "AWS-AWSManagedRulesAmazonIpReputationList",
                        Priority = 4,
                        OverrideAction = new OverrideActionProperty
                        {
                            None = new Dictionary<string, object> { }
                        },
                        Statement = new StatementProperty
                        {
                            ManagedRuleGroupStatement = new ManagedRuleGroupStatementProperty
                            {
                                VendorName = "AWS",
                                Name = "AWSManagedRulesAmazonIpReputationList"
                            }
                        },
                        VisibilityConfig = new VisibilityConfigProperty
                        {
                            CloudWatchMetricsEnabled = true,
                            MetricName = "AWS-AWSManagedRulesAmazonIpReputationList",
                            SampledRequestsEnabled = true
                        },
                    },

                    new RuleProperty
                    {
                        Name = "AWS-AWSManagedRulesAnonymousIpList",
                        Priority = 5,
                        OverrideAction = new OverrideActionProperty
                        {
                            None = new Dictionary<string, object> { }
                        },
                        Statement = new StatementProperty
                        {
                            ManagedRuleGroupStatement = new ManagedRuleGroupStatementProperty
                            {
                                VendorName = "AWS",
                                Name = "AWSManagedRulesAnonymousIpList"
                            }
                        },
                        VisibilityConfig = new VisibilityConfigProperty
                        {
                            CloudWatchMetricsEnabled = true,
                            MetricName = "AWS-AWSManagedRulesAnonymousIpList",
                            SampledRequestsEnabled = true
                        },
                    },

                    new RuleProperty
                    {
                        Name = "GeoLocationAPI-Regex-Rule",
                        Priority = 6,
                        Action = new RuleActionProperty
                        {
                            Allow = new AllowActionProperty()
                        },
                        Statement = new StatementProperty
                        {
                            OrStatement = new OrStatementProperty
                            {
                                Statements = new []
                                {
                                    new StatementProperty {
                                        RegexMatchStatement = new RegexMatchStatementProperty
                                        {
                                            RegexString = "((?i)\\/hc)",
                                            FieldToMatch = new FieldToMatchProperty
                                            {
                                                UriPath = new Dictionary<string, object> { }
                                            },
                                            TextTransformations = new [] { new TextTransformationProperty {
                                                Priority = 0,
                                                Type = "NONE" }
                                            }
                                        },
                                    },
                                    new StatementProperty {
                                        RegexMatchStatement = new RegexMatchStatementProperty
                                        {
                                            //AWS WAF supports the pattern syntax used by the PCRE library libpcre
                                            RegexString = "((?i)\\/api\\/V[0-9]\\/geolocation.*)",
                                            FieldToMatch = new FieldToMatchProperty
                                            {
                                                UriPath = new Dictionary<string, object> { }
                                            },
                                            TextTransformations = new [] { new TextTransformationProperty {
                                                Priority = 0,
                                                Type = "NONE"
                                            }}
                                        }
                                    }
                                }
                            }
                        },
                        VisibilityConfig = new VisibilityConfigProperty
                        {
                            CloudWatchMetricsEnabled = true,
                            MetricName = "GeoLocationAPI-Regex-Rule",
                            SampledRequestsEnabled = true
                        },
                    }
                }
            });

            // Create the WebACL LogGroup
            LogGroup geoLocationWebACLLogGroup = new LogGroup(this, "GeoLocationWebACLLogGroup", new LogGroupProps
            {
                //Check the AWS WAF Documentation
                //Log group names must start with aws-waf-logs-
                LogGroupName = "aws-waf-logs-geolocationapi",
                Retention = RetentionDays.SIX_MONTHS,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            // AWS WAF WAF has particular requirements to the naming and format of Logging Destination configs as described and shown in their docs.
            // Specifically, the ARN of the Log Group cannot end in :* which unfortunately is the return value for a Log Group ARN in Cloudformation.
            // Configuring the WebACL Log configuration and formatting the ARN
            CfnLoggingConfiguration geoLocationAPIWebACLLoggingConfiguration = new CfnLoggingConfiguration(this, "GeoLocationAPIWebACLLoggingConfiguration", new CfnLoggingConfigurationProps
            {
                LogDestinationConfigs = new[] {
                    Stack.Of(this).FormatArn(new ArnComponents
                    {
                        ArnFormat = ArnFormat.COLON_RESOURCE_NAME,
                        Service = "logs",
                        Resource = "log-group",
                        ResourceName = geoLocationWebACLLogGroup.LogGroupName
                    })},
                ResourceArn = geoLocationAPIWAFWebACL.AttrArn,
            });

            // Associate the WebAcl to ALB
            CfnWebACLAssociation geoLocationAPIWebACLAssocation = new CfnWebACLAssociation(this, "GeoLocationAPIWebACLAssocation", new CfnWebACLAssociationProps
            {
                ResourceArn = props.ALBResourceArn,
                WebAclArn = geoLocationAPIWAFWebACL.AttrArn
            });
        }
    }
}
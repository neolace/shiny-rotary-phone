import * as path from 'node:path';
import * as cdk from 'aws-cdk-lib';
import {
  type CfnStage,
  CorsHttpMethod,
  HttpApi,
  HttpMethod,
  HttpNoneAuthorizer,
  VpcLink,
} from 'aws-cdk-lib/aws-apigatewayv2';
import { HttpJwtAuthorizer } from 'aws-cdk-lib/aws-apigatewayv2-authorizers';
import { HttpAlbIntegration } from 'aws-cdk-lib/aws-apigatewayv2-integrations';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import * as ecs from 'aws-cdk-lib/aws-ecs';
import * as ecsPatterns from 'aws-cdk-lib/aws-ecs-patterns';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as logs from 'aws-cdk-lib/aws-logs';
import type { Construct } from 'constructs';

export interface EntraContext {
  readonly tenantId: string;
  readonly apiClientId: string;
  readonly audience: string;
  readonly allowedOrigins: string[];
}

export class EntraAuthStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const entra = readEntraContext(this);

    const origins = entra.allowedOrigins?.length ? entra.allowedOrigins : ['http://localhost:3000'];
    const issuer = `https://login.microsoftonline.com/${entra.tenantId}/v2.0`;
    const audiences = [...new Set([entra.audience, entra.apiClientId])];

    const vpc = new ec2.Vpc(this, 'Vpc', {
      maxAzs: 2,
      natGateways: 1,
      subnetConfiguration: [
        { name: 'public', subnetType: ec2.SubnetType.PUBLIC },
        { name: 'private', subnetType: ec2.SubnetType.PRIVATE_WITH_EGRESS },
      ],
    });

    const cluster = new ecs.Cluster(this, 'Cluster', {
      vpc,
      containerInsightsV2: ecs.ContainerInsights.ENABLED,
    });

    const service = new ecsPatterns.ApplicationLoadBalancedFargateService(this, 'ApiService', {
      cluster,
      publicLoadBalancer: false,
      openListener: false,
      desiredCount: 2,
      minHealthyPercent: 100,
      cpu: 256,
      memoryLimitMiB: 512,
      circuitBreaker: { rollback: true },
      taskImageOptions: {
        image: this.sampleImage(),
        containerPort: 8080,
        environment: {
          ASPNETCORE_ENVIRONMENT: 'Production',
          ASPNETCORE_URLS: 'http://+:8080',
          EntraApi__Instance: 'https://login.microsoftonline.com/',
          EntraApi__TenantId: entra.tenantId,
          EntraApi__ClientId: entra.apiClientId,
          EntraApi__Audience: entra.audience,
          EntraApi__AllowedTenants__0: entra.tenantId,
        },
      },
    });

    service.loadBalancer.connections.allowFrom(
      ec2.Peer.ipv4(vpc.vpcCidrBlock),
      ec2.Port.tcp(80),
      'HTTP from VPC (API Gateway VPC link)',
    );

    service.targetGroup.configureHealthCheck({
      path: '/health',
      healthyHttpCodes: '200',
      interval: cdk.Duration.seconds(30),
      timeout: cdk.Duration.seconds(5),
      healthyThresholdCount: 2,
      unhealthyThresholdCount: 3,
    });

    const vpcLink = new VpcLink(this, 'VpcLink', {
      vpc,
      subnets: { subnetType: ec2.SubnetType.PRIVATE_WITH_EGRESS },
    });

    const integration = new HttpAlbIntegration('AlbIntegration', service.listener, {
      vpcLink,
    });

    const jwtAuthorizer = new HttpJwtAuthorizer('EntraJwt', issuer, {
      jwtAudience: audiences,
      identitySource: ['$request.header.Authorization'],
    });

    const httpApi = new HttpApi(this, 'HttpApi', {
      apiName: 'entra-auth-api',
      description: 'Entra JWT-protected HTTP API in front of SampleApi on ECS',
      corsPreflight: {
        allowHeaders: ['Authorization', 'Content-Type'],
        allowMethods: [
          CorsHttpMethod.GET,
          CorsHttpMethod.POST,
          CorsHttpMethod.PUT,
          CorsHttpMethod.PATCH,
          CorsHttpMethod.DELETE,
          CorsHttpMethod.OPTIONS,
        ],
        allowOrigins: origins,
        maxAge: cdk.Duration.hours(1),
      },
    });

    httpApi.addRoutes({
      path: '/health',
      methods: [HttpMethod.GET],
      integration,
      authorizer: new HttpNoneAuthorizer(),
    });

    httpApi.addRoutes({
      path: '/{proxy+}',
      methods: [HttpMethod.ANY],
      integration,
      authorizer: jwtAuthorizer,
    });

    httpApi.addRoutes({
      path: '/',
      methods: [HttpMethod.ANY],
      integration,
      authorizer: jwtAuthorizer,
    });

    const accessLogs = new logs.LogGroup(this, 'HttpApiAccessLogs', {
      retention: logs.RetentionDays.ONE_MONTH,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });
    accessLogs.grants.write(new iam.ServicePrincipal('apigateway.amazonaws.com'));

    const defaultStage = httpApi.defaultStage?.node.defaultChild as CfnStage | undefined;
    if (defaultStage) {
      defaultStage.accessLogSettings = {
        destinationArn: accessLogs.logGroupArn,
        format: JSON.stringify({
          requestId: '$context.requestId',
          ip: '$context.identity.sourceIp',
          requestTime: '$context.requestTime',
          httpMethod: '$context.httpMethod',
          routeKey: '$context.routeKey',
          status: '$context.status',
          protocol: '$context.protocol',
          responseLength: '$context.responseLength',
          errorMessage: '$context.error.message',
          authorizerError: '$context.authorizer.error',
        }),
      };
    }

    new cdk.CfnOutput(this, 'ApiUrl', {
      value: httpApi.apiEndpoint,
      description: 'HTTP API base URL (append /health or /orders)',
    });
    new cdk.CfnOutput(this, 'EntraIssuer', { value: issuer });
    new cdk.CfnOutput(this, 'JwtAudiences', { value: audiences.join(',') });
  }

  private sampleImage(): ecs.ContainerImage {
    const registryImage: unknown = this.node.tryGetContext('sampleImage');
    if (typeof registryImage === 'string' && registryImage.length > 0) {
      return ecs.ContainerImage.fromRegistry(registryImage);
    }

    return ecs.ContainerImage.fromAsset(path.join(__dirname, '../..'), {
      file: 'samples/SampleApi/Dockerfile',
    });
  }
}

function readEntraContext(scope: Construct): EntraContext {
  const raw: unknown = scope.node.tryGetContext('entra');
  if (!isEntraContext(raw)) {
    throw new Error(
      'Set context.entra.{tenantId, apiClientId, audience} in infra/cdk.json (see docs/runbooks/entra-app-registrations.md).',
    );
  }
  return raw;
}

function isEntraContext(value: unknown): value is EntraContext {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const record = value as Record<string, unknown>;
  return (
    typeof record.tenantId === 'string' &&
    record.tenantId.length > 0 &&
    typeof record.apiClientId === 'string' &&
    record.apiClientId.length > 0 &&
    typeof record.audience === 'string' &&
    record.audience.length > 0
  );
}

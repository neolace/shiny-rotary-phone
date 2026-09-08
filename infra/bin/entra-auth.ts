#!/usr/bin/env node
import * as cdk from 'aws-cdk-lib';
import { EntraAuthStack } from '../lib/entra-auth-stack';

const app = new cdk.App();

new EntraAuthStack(app, 'EntraAuth', {
  description: 'HTTP API Gateway JWT authorizer (Entra) in front of a .NET API on ECS Fargate',
  env: {
    account: process.env.CDK_DEFAULT_ACCOUNT,
    region: process.env.CDK_DEFAULT_REGION,
  },
});

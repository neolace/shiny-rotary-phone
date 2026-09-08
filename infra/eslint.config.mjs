import eslint from '@eslint/js';
import { defineConfig } from 'eslint/config';
import cdkPlugin from 'eslint-plugin-awscdk';
import eslintConfigPrettier from 'eslint-config-prettier';
import tseslint from 'typescript-eslint';

export default defineConfig(
  {
    ignores: ['cdk.out/**', 'dist/**', 'node_modules/**'],
  },
  {
    files: ['**/*.{ts,mjs}'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommendedTypeChecked,
      cdkPlugin.configs.recommended,
      eslintConfigPrettier,
    ],
    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    rules: {
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/consistent-type-imports': 'error',
      'awscdk/require-jsdoc': 'off',
    },
  },
);

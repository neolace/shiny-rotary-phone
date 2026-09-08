function quote(file) {
  return `"${file.replaceAll('\\', '/')}"`;
}

function quoted(files) {
  return files.map(quote).join(' ');
}

export default {
  '*.cs': (files) =>
    `dotnet format EntraAuth.slnx --severity warn --include ${quoted(files)}`,
  'infra/{bin,lib}/**/*.ts': (files) => [
    `npx --prefix infra eslint --config infra/eslint.config.mjs --fix ${quoted(files)}`,
    `npx prettier --write ${quoted(files)}`,
  ],
  '*.{md,yml,yaml,json}': 'prettier --write --ignore-unknown',
};

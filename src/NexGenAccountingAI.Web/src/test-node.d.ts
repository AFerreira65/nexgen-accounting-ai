// O pouco do Node que os testes que leem ficheiros usam, sem trazer os tipos do Node para a aplicação.
declare module 'node:fs' {
  export function readFileSync(path: URL): Uint8Array
}

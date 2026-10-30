import type { components, paths } from './schema';

/** Types derived from the generated OpenAPI types (`schema.d.ts`, `npm run gen:api`): nothing here is written per endpoint. */
export type Schemas = components['schemas'];
export type ApiPath = keyof paths;
export type HttpMethod = 'get' | 'post';

/** The operation object of a path and method (`never` when the path has no such method). */
export type Operation<P extends ApiPath, M extends HttpMethod> = paths[P] extends { [K in M]: infer O } ? O : never;

/** The 200 JSON answer: a `ScreenResult<T>` for every twin. */
export type ResponseOf<O> = O extends { responses: { 200: { content: { 'application/json': infer R } } } } ? R : never;
export type BodyOf<O> = O extends { requestBody?: { content: { 'application/json': infer B } } } ? NonNullable<B> : never;
export type QueryOf<O> = O extends { parameters: { query?: infer Q } } ? NonNullable<Q> : never;
export type PathParamsOf<O> = O extends { parameters: { path?: infer Q } } ? NonNullable<Q> : never;

/** Request options of one operation: path values, query values and the JSON body, all typed from the document. */
export interface CallOptions<O> {
  path?: Partial<PathParamsOf<O>>;
  query?: Partial<QueryOf<O>>;
  body?: BodyOf<O>;
}

/** `'Ok' | 'Invalid' | 'NotFound' | 'Forbidden' | 'SignIn' | 'Next'`: the document describes the enum by name, as the wire carries it. */
export type ScreenStatus = Schemas['ScreenStatus'];
export type ScreenNext = Schemas['ScreenNext'];
export type SessionModel = Schemas['SessionModel'];

/** The shape every `ScreenResult<T>` shares, whatever `T` is (the generated types have one per model). */
export interface ScreenResult<T = unknown> {
  status?: ScreenStatus;
  model?: T;
  errors?: { [field: string]: string[] } | null;
  next?: ScreenNext;
  statusCode?: number | null;
}

import axios from 'axios'

const api = axios.create({
  baseURL: '/api'
})

export interface Provider {
  type: string
  name: string
  profileUrl: string
  iconUrl?: string
}

export interface ProvidersResponse {
  profileUrl: string
  providers: Provider[]
}

export interface Client {
  clientId: string
  name: string
  url: string
  logoUrl?: string
}

export interface ConsentInfo {
  client: Client
  scopes: string[]
  profileUrl: string
}

export interface RedirectResponse {
  redirectUrl: string
}

export async function getProviders(sessionId: string): Promise<ProvidersResponse> {
  const response = await api.get<ProvidersResponse>(`/auth/providers`, {
    params: { session_id: sessionId }
  })
  return response.data
}

export async function selectProvider(sessionId: string, providerType: string): Promise<RedirectResponse> {
  const response = await api.post<RedirectResponse>(`/auth/select-provider`, {
    sessionId,
    providerType
  })
  return response.data
}

export async function getConsentInfo(sessionId: string): Promise<ConsentInfo> {
  const response = await api.get<ConsentInfo>(`/auth/consent`, {
    params: { session_id: sessionId }
  })
  return response.data
}

export async function submitConsent(sessionId: string, approved: boolean): Promise<RedirectResponse> {
  const response = await api.post<RedirectResponse>(`/auth/consent`, {
    sessionId,
    approved
  })
  return response.data
}


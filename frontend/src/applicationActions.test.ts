import { describe, expect, it, vi } from 'vitest'

import {
  buildApplicationUrl,
  buildApplicationsUrl,
  createApplication,
  loadApplications,
  type CreateApplicationRequest,
} from './applicationActions'
import type { Application } from './pages/appShared'

describe('applicationActions', () => {
  it('builds application endpoint URLs', () => {
    expect(buildApplicationsUrl()).toBe('/api/applications')
    expect(buildApplicationUrl('app-1')).toBe('/api/applications/app-1')
  })

  it('loads applications from the API', async () => {
    const applications = [
      {
        id: 'app-1',
        applicationNumber: 'IND-001',
        sponsorName: 'Demo Sponsor',
        sequences: [],
      },
    ] satisfies Application[]
    const request = vi.fn().mockResolvedValue(applications)

    const result = await loadApplications(request)

    expect(request).toHaveBeenCalledWith('/api/applications')
    expect(result).toEqual(applications)
  })

  it('submits create application requests as JSON', async () => {
    const request = vi.fn().mockResolvedValue({ id: 'app-1' })
    const payload = {
      applicationNumber: 'IND-NEW',
      ectdTemplateKey: 'us-fda-ectd-3-2-2',
      sponsorName: 'Demo Sponsor',
      workingDirectoryParentPath: 'D:\\work',
    } satisfies CreateApplicationRequest

    await createApplication(payload, request)

    expect(request).toHaveBeenCalledWith('/api/applications', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  })
})

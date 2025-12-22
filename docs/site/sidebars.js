// @ts-check

/**
 * @type {import('@docusaurus/plugin-content-docs').SidebarsConfig}
 */
const sidebars = {
  gettingStartedSidebar: [
    {
      type: 'category',
      label: 'Getting Started',
      link: {
        type: 'generated-index',
        title: 'Getting Started',
        description: 'Get up and running with Ignixa FHIR Server.',
      },
      items: [
        'getting-started/installation',
        'getting-started/quick-start',
      ],
    },
  ],

  serverSidebar: [
    {
      type: 'category',
      label: 'Server Overview',
      link: {
        type: 'doc',
        id: 'server/overview',
      },
      items: [
        'server/architecture',
        'server/configuration',
        'server/multi-tenancy',
      ],
    },
    {
      type: 'category',
      label: 'FHIR Compliance',
      items: [
        'server/fhir/capability-statement',
        'server/fhir/supported-resources',
        'server/fhir/bundles',
        'server/fhir/search-parameters',
        'server/fhir/operations',
      ],
    },
    {
      type: 'category',
      label: 'Features',
      items: [
        'server/features/validation',
        'server/features/bulk-operations',
        'server/features/ttl',
        'server/features/mcp-server',
        'server/features/subscriptions',
      ],
    },
    {
      type: 'category',
      label: 'Deployment',
      items: [
        'server/deployment/docker',
        'server/deployment/azure',
      ],
    },
    {
      type: 'category',
      label: 'Security',
      items: [
        'server/security/authentication',
        'server/security/authorization',
      ],
    },
  ],

  coreSdkSidebar: [
    {
      type: 'category',
      label: 'Core SDK',
      link: {
        type: 'doc',
        id: 'core-sdk/overview',
      },
      items: [
        'core-sdk/abstractions',
        'core-sdk/serialization',
        'core-sdk/fhirpath',
        'core-sdk/validation',
        'core-sdk/search',
        'core-sdk/fhir-fakes',
        'core-sdk/package-management',
        'core-sdk/narrative-generator',
        'core-sdk/fhir-mapping-language',
        'core-sdk/sql-on-fhir',
        'core-sdk/firely-sdk-compatibility',
      ],
    },
  ],

  adrSidebar: [
    'adr/index',
  ],
};

export default sidebars;

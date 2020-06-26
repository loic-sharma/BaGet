import * as React from 'react';
import * as ReactDOM from 'react-dom';
import { StaticRouter as Router } from 'react-router-dom';
import { render, waitFor } from '@testing-library/react'
import fetch, { enableFetchMocks } from 'jest-fetch-mock'

import SearchResults from './SearchResults';

describe('Tests for SearchResults component', () => {
  beforeEach(() => {
    enableFetchMocks();
  });

  it('Should render without crashing', () => {
    const div = document.createElement('div');
    ReactDOM.render(<Router><SearchResults input='' /></Router>, div);
    ReactDOM.unmountComponentAtNode(div);
  });

  it('Should render a no package found when invalid name was given', async () => {
    fetch.mockResponse(JSON.stringify({
      data: []
    }));
    const { getByText } = render(<Router><SearchResults input='Baget.Unknown' /></Router>);

    const notFoundMessage = await waitFor(
      () => getByText('Oops, nothing here...')
    );

    expect(notFoundMessage).toBeDefined();
  });

  it('Should render a package when a valid name was given', async () => {
    const data = {
      data: [
        {
          id: 'BaGet.Authors',
          authors: 'BaGet.Authors',
          totalDownloads: 999,
          version: '1.0',
          tags: ['NuGet'],
          description: 'BaGet description here',
          iconUrl: ''
        }
      ]
    };
    fetch.mockResponseOnce(JSON.stringify(data));

    const { getByText } = render(<Router><SearchResults input={data.data[0].id} /></Router>);

    const packageList = await waitFor(
      () => getByText(data.data[0].id)
    );

    expect(packageList).toBeDefined();
  });
});
